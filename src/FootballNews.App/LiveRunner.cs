using System.Globalization;
using FootballNews.Core.Config;
using FootballNews.Core.Filtering;
using FootballNews.Core.Formatting;
using FootballNews.Core.Live;
using FootballNews.Core.Models;
using FootballNews.Core.Sources;
using FootballNews.Facebook;
using FootballNews.Rendering;
using FootballNews.Telegram;

namespace FootballNews.App;

/// <summary>
/// Job <c>live</c> (SPEC mục 13): đọc lệnh Telegram, theo dõi một trận, gửi bản nháp chờ duyệt và đăng lên Fanpage.
/// Chạy đến khi không còn việc hoặc hết <c>maxRuntime</c>; trạng thái nằm trong <c>data/live.json</c>.
/// </summary>
internal sealed class LiveRunner(
    TelegramClient telegram,
    long adminId,
    FacebookClient? facebook,
    IMatchSource espn,
    IReadOnlyList<Competition> competitions,
    ClubCatalog clubs,
    LiveConfig config,
    LiveStore store,
    LiveCardRenderer renderer,
    GitCommitter git,
    TimeSpan maxRuntime)
{
    // Bắt đầu hỏi ESPN từ 15 phút trước giờ đá.
    private static readonly TimeSpan WarmUp = TimeSpan.FromMinutes(15);
    private const int LongPollSeconds = 25;
    // Chạy bằng tay: nghe lệnh ít nhất chừng này kể từ lúc bật hoặc từ tin nhắn gần nhất của chủ trang.
    private static readonly TimeSpan InteractionWindow = TimeSpan.FromMinutes(5);
    private const int MaxSearchResults = 8;
    private const int MaxLiveButtons = 20;
    // /live liệt kê cả trận sắp đá trong chừng này giờ.
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromHours(3);

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly Dictionary<int, byte[]> _images = [];
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private LiveState _state = new();
    private long? _offset;
    private DateTimeOffset _lastInteraction = DateTimeOffset.UtcNow;
    private bool _dirty;

    // Chủ trang gõ /stop: tắt ngay, không nghe lệnh thêm và không nối lượt chạy mới.
    private bool _stopRequested;

    private TimeSpan DraftLifetime => TimeSpan.FromMinutes(config.DraftMinutes);
    private TimeSpan PollInterval => TimeSpan.FromSeconds(config.PollSeconds);
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>
    /// Chạy đến khi xong việc. Trả <c>true</c> khi phải dừng vì hết giới hạn thời gian mà trận vẫn đang theo dõi:
    /// workflow sẽ tự bật lượt chạy mới để chạy tiếp.
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        _state = store.Load();
        var deadline = _startedAt + maxRuntime;
        var nextPoll = DateTimeOffset.MinValue;
        var continuation = false;

        try
        {
            await ReadUpdatesAsync(0, ct);
            while (!_stopRequested && !ct.IsCancellationRequested && Now < deadline)
            {
                var watch = _state.Watch;
                if (watch is null)
                {
                    // Chưa chọn trận: nghe lệnh một lúc rồi tắt.
                    if (Now - _lastInteraction < InteractionWindow)
                    {
                        await ReadUpdatesAsync(LongPollSeconds, ct);
                        continue;
                    }
                    Console.WriteLine("Không theo dõi trận nào.");
                    break;
                }
                if (Now < watch.KickoffUtc - WarmUp)
                {
                    // Đã chọn trận nhưng chưa đến giờ: chạy tiếp và chờ, vẫn trả lời lệnh trong lúc chờ.
                    var untilStart = (int)(watch.KickoffUtc - WarmUp - Now).TotalSeconds;
                    await ReadUpdatesAsync(Math.Clamp(untilStart, 1, LongPollSeconds), ct);
                    continue;
                }

                // ESPN bị kẹt, quá lâu sau giờ bóng lăn vẫn chưa báo kết thúc: bỏ theo dõi để không chạy mãi.
                if (!watch.Finished && Now >= config.WatchDeadline(watch))
                {
                    var limit = config.WatchDeadline(watch) - watch.KickoffUtc;
                    var duration = limit.Minutes > 0 ? $"{(int)limit.TotalHours} tiếng {limit.Minutes} phút" : $"{(int)limit.TotalHours} tiếng";
                    await NotifyAsync($"⚠️ ESPN chưa báo kết thúc trận {MatchName(watch)} sau {duration} kể từ giờ bóng lăn. Bot dừng theo dõi.", ct);
                    await StopWatchAsync("🚫 Bot dừng theo dõi vì quá giờ, bản nháp không được đăng.", ct);
                    break;
                }

                if (Now >= nextPoll)
                {
                    await PollAsync(ct);
                    nextPoll = Now + PollInterval;
                }
                await AdvanceQueueAsync(ct);

                if (_state.Watch is { Finished: true } && !_state.HasPending)
                {
                    await FinishWatchAsync(ct);
                    break;
                }
                if (_state.Watch is null)
                    break;

                // Chờ tin nhắn đến lần hỏi ESPN kế tiếp hoặc bản nháp đang chờ hết hạn, cái nào sớm hơn.
                var wait = Math.Min(LongPollSeconds, Math.Max(1, (int)(Earliest(nextPoll) - Now).TotalSeconds));
                await ReadUpdatesAsync(wait, ct);
            }

            if (_state.Watch is not null && Now >= deadline)
            {
                await NotifyAsync("⏳ Hết giới hạn thời gian của một lượt chạy GitHub. Bot tự bật lượt mới để theo dõi tiếp, mất khoảng 1–2 phút.", ct);
                continuation = true;
            }

            // Đánh dấu đã đọc mọi tin đã xử lý.
            if (_offset is not null)
            {
                try
                {
                    await telegram.GetUpdatesAsync(_offset, 0, ct);
                }
                catch (TelegramException ex)
                {
                    Console.WriteLine($"Không đánh dấu được tin đã đọc: {ex.Message}");
                }
            }
        }
        finally
        {
            await SaveAsync("chore: update live.json", force: true);
        }
        return continuation;
    }

    private DateTimeOffset Earliest(DateTimeOffset nextPoll)
    {
        var active = _state.Active;
        var expiry = active is null ? DateTimeOffset.MaxValue : active.CreatedAt + DraftLifetime;
        return expiry < nextPoll ? expiry : nextPoll;
    }

    // ───────────────────────── Theo dõi trận ─────────────────────────

    private async Task PollAsync(CancellationToken ct)
    {
        var watch = _state.Watch!;
        var competition = FindCompetition(watch.Competition);
        Match? match;
        try
        {
            match = await espn.GetMatchAsync(competition, watch.MatchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"ESPN lỗi khi lấy trận {watch.MatchId}: {ex.Message}");
            return;
        }
        if (match is null)
            return;

        if (match.Status is MatchStatus.Postponed or MatchStatus.Cancelled)
        {
            await NotifyAsync($"⚠️ Trận {MatchName(watch)} bị {(match.Status == MatchStatus.Postponed ? "hoãn" : "huỷ")}. Bot dừng theo dõi.", ct);
            await StopWatchAsync("🚫 Trận bị hoãn/huỷ, bản nháp không được đăng.", ct);
            return;
        }

        watch.KickoffUtc = match.KickoffUtc;
        var update = Session(competition).Apply(match, Now);
        Console.WriteLine($"{Now:HH:mm:ss} {match.StatusName} {match.Home.Score}-{match.Away.Score}: {update.NewDrafts.Count} bản nháp mới.");
        if (update.NewDrafts.Count > 0 || update.VarWarnings.Count > 0)
            _dirty = true;

        foreach (var draft in update.VarWarnings.Where(d => d.Status == DraftStatus.Active))
            await RefreshPreviewAsync(draft, ct);
        await SaveAsync(null);
    }

    private async Task AdvanceQueueAsync(CancellationToken ct)
    {
        var expired = _state.ExpireDue(Now, DraftLifetime);
        foreach (var draft in expired.Where(d => d.TelegramMessageId is not null))
            await EditDraftMessageAsync(draft, $"⌛ Hết hạn, không đăng.", ct);
        var skipped = expired.Where(d => d.TelegramMessageId is null).ToList();
        if (skipped.Count > 0)
            await NotifyAsync($"⌛ Bỏ qua {skipped.Count} bản nháp hết hạn khi còn trong hàng đợi: {string.Join(", ", skipped.Select(d => $"#{d.Id} {LiveSession.KindLabel(d.Kind)}"))}.", ct);
        if (expired.Count > 0)
            _dirty = true;

        var next = _state.PromoteNext();
        if (next is not null)
        {
            _dirty = true;
            await SendPreviewAsync(next, ct);
        }
        await SaveAsync(null);
    }

    private async Task FinishWatchAsync(CancellationToken ct)
    {
        var watch = _state.Watch!;
        var posted = _state.Drafts.Count(d => d.Status == DraftStatus.Posted);
        await NotifyAsync($"🏁 Đã theo dõi xong {MatchName(watch)}. Đã đăng {posted} bài.", ct);
        _state.Clear();
        _images.Clear();
        _dirty = true;
        await SaveAsync($"chore: finish live watch {watch.MatchId}", force: true);
    }

    private async Task StopWatchAsync(string reason, CancellationToken ct)
    {
        foreach (var draft in _state.Drafts.Where(d => d.IsPending))
        {
            draft.Status = DraftStatus.Cancelled;
            if (draft.TelegramMessageId is not null)
                await EditDraftMessageAsync(draft, reason, ct);
        }
        _state.Clear();
        _images.Clear();
        _dirty = true;
        await SaveAsync("chore: stop live watch", force: true);
    }

    // ───────────────────────── Bản nháp ─────────────────────────

    private async Task SendPreviewAsync(Draft draft, CancellationToken ct)
    {
        var image = await RenderAsync(draft);
        var caption = PreviewCaption(draft);
        if (image is not null)
        {
            draft.TelegramMessageId = await telegram.SendPhotoAsync(image, $"draft-{draft.Id}.png", caption, DraftButtons(draft), ct);
            draft.TelegramIsPhoto = true;
        }
        else
        {
            draft.TelegramMessageId = await telegram.SendMessageAsync("⚠️ Không vẽ được ảnh, bài sẽ đăng dạng chữ.\n\n" + caption, DraftButtons(draft), ct);
            draft.TelegramIsPhoto = false;
        }
        _dirty = true;
    }

    /// <summary>Cập nhật chữ của bản nháp đang chờ (vd vừa có cảnh báo VAR), giữ nguyên nút.</summary>
    private Task RefreshPreviewAsync(Draft draft, CancellationToken ct) =>
        draft.TelegramMessageId is { } id
            ? telegram.EditAsync(id, draft.TelegramIsPhoto, PreviewCaption(draft), DraftButtons(draft), ct)
            : Task.CompletedTask;

    /// <summary>Đổi dòng đầu của tin nháp thành kết quả xử lý và gỡ nút.</summary>
    private async Task EditDraftMessageAsync(Draft draft, string status, CancellationToken ct)
    {
        try
        {
            await telegram.EditAsync(draft.TelegramMessageId!.Value, draft.TelegramIsPhoto, $"{status}\n──────────\n{draft.Caption}", null, ct);
        }
        catch (TelegramException ex)
        {
            Console.WriteLine($"Không sửa được tin nháp #{draft.Id}: {ex.Message}");
        }
    }

    private string PreviewCaption(Draft draft)
    {
        var lines = new List<string> { $"📝 Bản nháp #{draft.Id} · {LiveSession.KindLabel(draft.Kind)}" };
        if (draft.VarWarning)
            lines.Add("⚠️ VAR đã huỷ bàn này. Cân nhắc trước khi đăng.");
        var expiry = (draft.CreatedAt + DraftLifetime).ToOffset(ReportWindow.VietnamOffset);
        lines.Add($"⏰ Hết hạn lúc {expiry.ToString("HH:mm", Invariant)}");
        var queued = _state.Drafts.Count(d => d.Status == DraftStatus.Queued);
        if (queued > 0)
            lines.Add($"📥 Còn {queued} bản nháp xếp hàng phía sau");
        lines.Add("──────────");
        lines.Add(draft.Caption);
        return string.Join("\n", lines);
    }

    private static IReadOnlyList<IReadOnlyList<InlineButton>> DraftButtons(Draft draft) =>
    [
        [new InlineButton("✅ Đăng", $"p|{draft.Id}"), new InlineButton("❌ Bỏ", $"s|{draft.Id}"), new InlineButton("✏️ Sửa", $"e|{draft.Id}")],
    ];

    private async Task<byte[]?> RenderAsync(Draft draft)
    {
        if (_images.TryGetValue(draft.Id, out var cached))
            return cached;
        var watch = _state.Watch;
        if (watch is null)
            return null;
        var (from, to) = config.Gradient(draft.Kind);
        var card = new LiveCard(
            FindCompetition(watch.Competition).Name,
            watch.Home,
            watch.Away,
            clubs.DisplayName(watch.Home),
            clubs.DisplayName(watch.Away),
            draft.HomeScore,
            draft.AwayScore,
            draft.Title,
            draft.Detail,
            draft.CustomLines,
            from,
            to,
            draft.HomeLines,
            draft.AwayLines,
            RedCardIcon: draft.Kind == LiveEventKind.RedCard,
            TeamId: draft.TeamId);
        try
        {
            var png = await renderer.RenderAsync(card);
            _images[draft.Id] = png;
            return png;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Không vẽ được ảnh bản nháp #{draft.Id}: {ex.Message}");
            return null;
        }
    }

    private async Task PostAsync(Draft draft, CancellationToken ct)
    {
        if (facebook is null)
        {
            await NotifyAsync("❌ Chưa cài FB_PAGE_ID và FB_PAGE_TOKEN nên không đăng được. Cài secret rồi bấm ✅ Đăng lại.", ct);
            return;
        }
        try
        {
            var image = await RenderAsync(draft);
            var postId = image is not null
                ? await facebook.PostPhotoAsync(image, draft.Caption, ct)
                : await facebook.PostTextAsync(draft.Caption, ct);
            draft.Status = DraftStatus.Posted;
            draft.FacebookPostId = postId;
            if (_state.EditingDraftId == draft.Id)
                _state.EditingDraftId = null;
            _dirty = true;
            var time = Now.ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm", Invariant);
            await EditDraftMessageAsync(draft, $"✅ Đã đăng lúc {time}\n{FacebookClient.PostUrl(postId)}", ct);
            // Lưu và đẩy lên repo ngay: job chết giữa chừng thì lần sau không đăng lại bài này.
            await SaveAsync($"chore: posted live draft {draft.Id}", force: true);
        }
        catch (FacebookException ex)
        {
            await NotifyAsync($"❌ Đăng Facebook lỗi (bản nháp #{draft.Id}): {ex.Message}\nBản nháp vẫn giữ nguyên, sửa xong bấm ✅ Đăng lại.", ct);
        }
    }

    // ───────────────────────── Telegram ─────────────────────────

    private async Task ReadUpdatesAsync(int timeoutSeconds, CancellationToken ct)
    {
        IReadOnlyList<TelegramUpdate> updates;
        try
        {
            updates = await telegram.GetUpdatesAsync(_offset, timeoutSeconds, ct);
        }
        catch (TelegramException ex)
        {
            Console.WriteLine($"Đọc tin Telegram lỗi: {ex.Message}");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return;
        }

        foreach (var update in updates)
        {
            // Tin đến sau /stop để lại cho lượt chạy sau.
            if (_stopRequested)
                break;
            _offset = update.UpdateId + 1;
            try
            {
                if (update.Callback is { } callback)
                    await HandleCallbackAsync(callback, ct);
                else if (update.Message is { Text: not null } message)
                    await HandleMessageAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Lỗi khi xử lý tin {update.UpdateId}: {ex}");
                await NotifyAsync($"❌ Lỗi khi xử lý lệnh: {ex.GetType().Name}: {ex.Message}", ct);
            }
        }
        await SaveAsync(null);
    }

    private bool IsAdmin(long fromId, long chatId)
    {
        if (fromId != adminId || chatId != adminId)
            return false;
        _lastInteraction = Now;
        return true;
    }

    private async Task HandleMessageAsync(TelegramMessage message, CancellationToken ct)
    {
        var text = message.Text!.Trim();
        var (command, argument) = ParseCommand(text);

        // Lệnh duy nhất trả lời cả người lạ: để chủ trang lấy user id khi cài đặt.
        if (command == "/whoami")
        {
            await telegram.SendMessageAsync($"User id của bạn: {message.FromId}", null, ct, message.ChatId.ToString(Invariant));
            return;
        }
        if (!IsAdmin(message.FromId, message.ChatId))
        {
            Console.WriteLine($"Bỏ qua tin từ người lạ {message.FromId}.");
            return;
        }

        switch (command)
        {
            case "/start" or "/help":
                await NotifyAsync(HelpText, ct);
                break;
            case "/live":
                await LiveListAsync(ct);
                break;
            case "/watch":
                await WatchSearchAsync(argument, ct);
                break;
            case "/list":
                await NotifyAsync(ListText(), ct);
                break;
            case "/unwatch":
                if (_state.Watch is null)
                {
                    await NotifyAsync("Chưa theo dõi trận nào.", ct);
                    break;
                }
                var name = MatchName(_state.Watch);
                await StopWatchAsync("🚫 Đã bỏ theo dõi, không đăng.", ct);
                await NotifyAsync($"👋 Đã bỏ theo dõi {name}.", ct);
                break;
            case "/stop":
                if (_state.Watch is { } watching)
                {
                    var stopped = MatchName(watching);
                    await StopWatchAsync("🛑 Đã dừng bot, không đăng.", ct);
                    await NotifyAsync($"🛑 Đã bỏ theo dõi {stopped} và tắt bot. Muốn dùng lại thì bấm Run workflow.", ct);
                }
                else
                {
                    await NotifyAsync("🛑 Đã tắt bot. Muốn dùng lại thì bấm Run workflow.", ct);
                }
                _stopRequested = true;
                break;
            case "/fbtest":
                await FacebookTestAsync(ct);
                break;
            case null:
                await HandleEditTextAsync(message, text, ct);
                break;
            default:
                await NotifyAsync($"Không hiểu lệnh {command}.\n\n{HelpText}", ct);
                break;
        }
    }

    private async Task HandleEditTextAsync(TelegramMessage message, string text, CancellationToken ct)
    {
        var draft = message.ReplyToMessageId is { } replyTo ? _state.FindByMessage(replyTo) : null;
        draft ??= _state.EditingDraftId is { } editing ? _state.Find(editing) : null;
        if (draft is null)
        {
            await NotifyAsync("Không có bản nháp nào đang chờ sửa. Bấm ✏️ Sửa dưới bản nháp, hoặc trả lời (reply) thẳng vào tin nháp.", ct);
            return;
        }
        if (draft.Status != DraftStatus.Active)
        {
            await NotifyAsync($"Bản nháp #{draft.Id} không còn chờ duyệt nên không sửa được.", ct);
            return;
        }

        var oldMessage = draft.TelegramMessageId;
        var oldIsPhoto = draft.TelegramIsPhoto;
        LiveSession.ApplyEdit(draft, text);
        _images.Remove(draft.Id);
        _state.EditingDraftId = null;
        _dirty = true;

        if (oldMessage is { } id)
        {
            try
            {
                await telegram.EditAsync(id, oldIsPhoto, $"✏️ Đã sửa, xem bản mới bên dưới.\n──────────\n{draft.Caption}", null, ct);
            }
            catch (TelegramException ex)
            {
                Console.WriteLine($"Không sửa được tin cũ: {ex.Message}");
            }
        }
        await SendPreviewAsync(draft, ct);
        await SaveAsync(null);
    }

    private async Task HandleCallbackAsync(TelegramCallback callback, CancellationToken ct)
    {
        if (!IsAdmin(callback.FromId, callback.ChatId))
        {
            Console.WriteLine($"Bỏ qua nút bấm của người lạ {callback.FromId}.");
            return;
        }

        var parts = callback.Data.Split('|');
        if (parts[0] == "w" && parts.Length == 3)
        {
            await telegram.AnswerCallbackAsync(callback.Id, null, ct);
            await StartWatchAsync(parts[1], parts[2], ct);
            return;
        }

        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, Invariant, out var draftId) || _state.Find(draftId) is not { } draft)
        {
            await telegram.AnswerCallbackAsync(callback.Id, "Bản nháp này không còn nữa.", ct);
            return;
        }

        // Bấm đúng lúc vừa hết hạn: tính hạn trước khi làm gì.
        await AdvanceQueueAsync(ct);
        if (draft.Status != DraftStatus.Active)
        {
            await telegram.AnswerCallbackAsync(callback.Id, StatusText(draft.Status), ct);
            return;
        }

        switch (parts[0])
        {
            case "p":
                await telegram.AnswerCallbackAsync(callback.Id, "Đang đăng…", ct);
                await PostAsync(draft, ct);
                break;
            case "s":
                draft.Status = DraftStatus.Skipped;
                if (_state.EditingDraftId == draft.Id)
                    _state.EditingDraftId = null;
                _dirty = true;
                await telegram.AnswerCallbackAsync(callback.Id, "Đã bỏ.", ct);
                await EditDraftMessageAsync(draft, "❌ Đã bỏ, không đăng.", ct);
                break;
            case "e":
                _state.EditingDraftId = draft.Id;
                _dirty = true;
                await telegram.AnswerCallbackAsync(callback.Id, null, ct);
                await NotifyAsync(
                    $"✏️ Gửi nội dung mới cho bản nháp #{draft.Id}.\nMỗi dòng là một dòng chữ trên ảnh; cả đoạn cũng là caption (hashtag giữ nguyên).\nHết hạn lúc {(draft.CreatedAt + DraftLifetime).ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm", Invariant)}.",
                    ct);
                break;
            default:
                await telegram.AnswerCallbackAsync(callback.Id, null, ct);
                break;
        }
        await AdvanceQueueAsync(ct);
    }

    private async Task WatchSearchAsync(string query, CancellationToken ct)
    {
        if (_state.Watch is { } current)
        {
            await NotifyAsync($"Đang theo dõi {MatchName(current)}. Gõ /unwatch trước rồi mới chọn trận khác.", ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            await NotifyAsync("Gõ kèm tên đội, vd: /watch arsenal hull", ct);
            return;
        }

        await NotifyAsync($"🔎 Đang tìm \"{query}\"…", ct);
        var matches = await SearchAsync(query, ct);
        if (matches.Count == 0)
        {
            await NotifyAsync($"Không tìm thấy trận nào khớp \"{query}\" từ hôm nay đến {ReportWindow.UpcomingDays} ngày tới.", ct);
            return;
        }

        var buttons = matches.Select(m => (IReadOnlyList<InlineButton>)
            [new InlineButton(
                $"{clubs.DisplayName(m.Home.Team)} – {clubs.DisplayName(m.Away.Team)} · {m.KickoffUtc.ToOffset(ReportWindow.VietnamOffset).ToString("dd/MM HH:mm", Invariant)}{(m.Status == MatchStatus.Live ? " · đang đá" : "")}",
                $"w|{m.Competition.Slug}|{m.Id}")]).ToList();
        await telegram.SendMessageAsync("Chọn trận cần theo dõi:", buttons, ct);
    }

    private async Task<IReadOnlyList<Match>> SearchAsync(string query, CancellationToken ct)
    {
        var now = Now;
        var today = ReportWindow.VietnamToday(now);
        var end = new DateTimeOffset(today.AddDays(ReportWindow.UpcomingDays + 1).ToDateTime(TimeOnly.MinValue), ReportWindow.VietnamOffset);
        // Ngày của ESPN theo giờ Mỹ nên lấy dư một ngày phía trước.
        var all = await FetchAsync(Enumerable.Range(-1, ReportWindow.UpcomingDays + 2).Select(i => today.AddDays(i)), ct);

        return all
            .Where(m => m.Status is MatchStatus.Scheduled or MatchStatus.Live)
            .Where(m => m.KickoffUtc >= now.AddHours(-3) && m.KickoffUtc < end)
            .Where(m => TextSearch.MatchesAllWords(query,
                $"{m.Home.Team.Name} {clubs.DisplayName(m.Home.Team)} {m.Away.Team.Name} {clubs.DisplayName(m.Away.Team)}"))
            .OrderBy(m => m.KickoffUtc)
            .Take(MaxSearchResults)
            .ToList();
    }

    /// <summary>Lịch của mọi giải (cả cúp trong live.json) trong các ngày đã cho, bỏ trận trùng.</summary>
    private async Task<IReadOnlyList<Match>> FetchAsync(IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        var days = dates.ToList();
        using var gate = new SemaphoreSlim(8);
        var tasks = competitions.SelectMany(c => days.Select(async date =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await espn.GetMatchesAsync(c, date, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"ESPN lỗi khi tìm {c.Slug} {date}: {ex.Message}");
                return (IReadOnlyList<Match>)[];
            }
            finally
            {
                gate.Release();
            }
        }));
        return (await Task.WhenAll(tasks)).SelectMany(x => x).DistinctBy(m => m.Id).ToList();
    }

    /// <summary>/live: trận đang đá và sắp đá trong vài giờ tới, kèm nút để chọn theo dõi.</summary>
    private async Task LiveListAsync(CancellationToken ct)
    {
        await NotifyAsync("🔎 Đang lấy các trận đang đá và sắp đá…", ct);
        var now = Now;
        var today = ReportWindow.VietnamToday(now);
        var all = await FetchAsync([today.AddDays(-1), today, today.AddDays(1)], ct);

        var live = all.Where(m => m.Status == MatchStatus.Live).OrderBy(m => m.KickoffUtc).ToList();
        var soon = all
            .Where(m => m.Status == MatchStatus.Scheduled && m.KickoffUtc >= now.AddMinutes(-15) && m.KickoffUtc <= now + UpcomingWindow)
            .OrderBy(m => m.KickoffUtc)
            .ToList();
        if (live.Count == 0 && soon.Count == 0)
        {
            await NotifyAsync($"Không có trận nào đang đá hoặc đá trong {UpcomingWindow.TotalHours:0} giờ tới. Dùng /watch <tên đội> để tìm trận các ngày tới.", ct);
            return;
        }

        var lines = new List<string>();
        if (live.Count > 0)
        {
            lines.Add($"🔴 ĐANG ĐÁ ({live.Count})");
            lines.AddRange(live.Select(m => $"• {m.Competition.Name}: {clubs.DisplayName(m.Home.Team)} {m.Home.Score}-{m.Away.Score} {clubs.DisplayName(m.Away.Team)}{(m.StatusName == "STATUS_HALFTIME" ? " (nghỉ giữa hiệp)" : "")}"));
        }
        if (soon.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add($"🕒 SẮP ĐÁ TRONG {UpcomingWindow.TotalHours:0} GIỜ ({soon.Count})");
            lines.AddRange(soon.Select(m => $"• {VietnamTime(m.KickoffUtc)} {m.Competition.Name}: {clubs.DisplayName(m.Home.Team)} – {clubs.DisplayName(m.Away.Team)}"));
        }

        IReadOnlyList<IReadOnlyList<InlineButton>>? buttons = null;
        if (_state.Watch is { } current)
        {
            lines.Add("");
            lines.Add($"Đang theo dõi {MatchName(current)}. Gõ /unwatch nếu muốn chọn trận khác.");
        }
        else
        {
            lines.Add("");
            lines.Add("Bấm nút bên dưới để chọn trận theo dõi.");
            buttons = live.Select(m => $"🔴 {clubs.DisplayName(m.Home.Team)} {m.Home.Score}-{m.Away.Score} {clubs.DisplayName(m.Away.Team)}")
                .Zip(live, (text, m) => (Text: text, Match: m))
                .Concat(soon.Select(m => (Text: $"🕒 {VietnamTime(m.KickoffUtc)} {clubs.DisplayName(m.Home.Team)} – {clubs.DisplayName(m.Away.Team)}", Match: m)))
                .Take(MaxLiveButtons)
                .Select(x => (IReadOnlyList<InlineButton>)[new InlineButton(x.Text, $"w|{x.Match.Competition.Slug}|{x.Match.Id}")])
                .ToList();
        }
        await telegram.SendMessageAsync(string.Join("\n", lines), buttons, ct);
    }

    private static string VietnamTime(DateTimeOffset utc) => utc.ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm", Invariant);

    private async Task StartWatchAsync(string slug, string matchId, CancellationToken ct)
    {
        if (_state.Watch is { } current)
        {
            await NotifyAsync($"Đang theo dõi {MatchName(current)}. Gõ /unwatch trước rồi mới chọn trận khác.", ct);
            return;
        }
        var competition = competitions.FirstOrDefault(c => c.Slug == slug);
        var match = competition is null ? null : await espn.GetMatchAsync(competition, matchId, ct);
        if (match is null || match.Status is not (MatchStatus.Scheduled or MatchStatus.Live))
        {
            await NotifyAsync("Trận này không còn theo dõi được (đã xong, hoãn hoặc không tìm thấy).", ct);
            return;
        }

        _state.Clear();
        _state.Watch = LiveTracker.Start(match);
        _dirty = true;
        var kickoff = match.KickoffUtc.ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm dd/MM", Invariant);
        var status = match.Status == MatchStatus.Live
            ? $"Trận đang đá, tỉ số hiện tại {match.Home.Score}-{match.Away.Score}. Bot chỉ báo các sự kiện từ bây giờ."
            : $"Đá lúc {kickoff}. Bot đang chạy và sẽ chờ đến 15 phút trước giờ đá thì bắt đầu theo dõi, không cần bấm chạy lại.";
        await NotifyAsync($"👀 Đã chọn theo dõi {MatchName(_state.Watch)} ({competition!.Name}).\n{status}", ct);
        await SaveAsync($"chore: start live watch {match.Id}", force: true);
    }

    private async Task FacebookTestAsync(CancellationToken ct)
    {
        if (facebook is null)
        {
            await NotifyAsync("❌ Chưa cài FB_PAGE_ID và FB_PAGE_TOKEN.", ct);
            return;
        }
        try
        {
            var time = Now.ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm dd/MM/yyyy", Invariant);
            var postId = await facebook.PostTextAsync($"🧪 Bài thử của Matchday Live Bot ({time}). Bài này sẽ được xoá.", ct);
            await NotifyAsync($"✅ Đã đăng bài thử: {FacebookClient.PostUrl(postId)}\nMở link bằng cửa sổ ẩn danh: xem được là người ngoài cũng thấy. Kiểm tra xong thì xoá bài.", ct);
        }
        catch (FacebookException ex)
        {
            await NotifyAsync($"❌ Đăng thử lỗi: {ex.Message}", ct);
        }
    }

    // ───────────────────────── Tiện ích ─────────────────────────

    private const string HelpText = """
        ⚽ Matchday Live Bot
        /live — các trận đang đá và sắp đá trong 3 giờ tới, bấm để theo dõi
        /watch <tên đội> — tìm trận để theo dõi, vd /watch arsenal hull
        /list — trận đang theo dõi và bản nháp
        /unwatch — bỏ theo dõi
        /stop — bỏ theo dõi và tắt bot ngay
        /fbtest — đăng thử một bài lên Fanpage
        /whoami — xem user id

        Mỗi sự kiện (bàn thắng, thẻ đỏ, hết hiệp 1, hết trận, VAR huỷ bàn) là một bản nháp chờ duyệt.
        ✅ Đăng · ❌ Bỏ · ✏️ Sửa (hoặc reply vào bản nháp bằng nội dung mới). Bản nháp hết hạn sau 10 phút.
        """;

    private string ListText()
    {
        if (_state.Watch is not { } w)
            return "Chưa theo dõi trận nào. Gõ /watch <tên đội> để chọn trận.";
        var kickoff = w.KickoffUtc.ToOffset(ReportWindow.VietnamOffset).ToString("HH:mm dd/MM", Invariant);
        var lines = new List<string>
        {
            $"👀 {MatchName(w)} ({FindCompetition(w.Competition).Name}), đá lúc {kickoff}",
            $"Tỉ số bot đã thấy: {w.HomeScore}-{w.AwayScore}",
        };
        if (_state.Drafts.Count == 0)
            lines.Add("Chưa có bản nháp nào.");
        foreach (var d in _state.Drafts)
            lines.Add($"#{d.Id} {LiveSession.KindLabel(d.Kind)} {d.HomeScore}-{d.AwayScore}: {StatusText(d.Status)}");
        return string.Join("\n", lines);
    }

    private static string StatusText(DraftStatus status) => status switch
    {
        DraftStatus.Queued => "đang xếp hàng",
        DraftStatus.Active => "đang chờ duyệt",
        DraftStatus.Posted => "đã đăng",
        DraftStatus.Skipped => "đã bỏ",
        DraftStatus.Expired => "đã hết hạn",
        DraftStatus.Cancelled => "đã huỷ",
        _ => status.ToString(),
    };

    private static (string? Command, string Argument) ParseCommand(string text)
    {
        if (!text.StartsWith('/'))
            return (null, text);
        var space = text.IndexOfAny([' ', '\n']);
        var command = space < 0 ? text : text[..space];
        var at = command.IndexOf('@');
        if (at > 0)
            command = command[..at];
        return (command.ToLowerInvariant(), space < 0 ? "" : text[(space + 1)..].Trim());
    }

    private LiveSession Session(Competition competition) => new(_state, clubs, competition, config.Hashtag);

    private string MatchName(WatchedMatch w) => $"{clubs.DisplayName(w.Home)} – {clubs.DisplayName(w.Away)}";

    private Competition FindCompetition(string slug) =>
        competitions.FirstOrDefault(c => c.Slug == slug)
        ?? new Competition(slug, slug.ToUpperInvariant(), slug.ToUpperInvariant(), "", "", 999, []);

    private async Task NotifyAsync(string text, CancellationToken ct)
    {
        try
        {
            await telegram.SendMessageAsync(text, null, ct);
        }
        catch (TelegramException ex)
        {
            Console.WriteLine($"Không gửi được tin Telegram: {ex.Message}\n{text}");
        }
    }

    /// <summary>Ghi <c>live.json</c> khi có thay đổi. <paramref name="commitMessage"/> thì đẩy luôn lên repo.</summary>
    private async Task SaveAsync(string? commitMessage, bool force = false)
    {
        if (_dirty)
        {
            store.Save(_state);
            _dirty = false;
        }
        if (commitMessage is not null && force)
            await git.CommitAsync(store.Path, commitMessage);
    }
}

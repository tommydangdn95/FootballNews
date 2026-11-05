using FootballNews.Core.Config;
using FootballNews.Core.Dedup;
using FootballNews.Core.Filtering;
using FootballNews.Core.Formatting;
using FootballNews.Core.Models;
using FootballNews.Core.Sources;
using FootballNews.Rendering;

namespace FootballNews.App;

internal sealed class DailyRunner(
    IMatchSource source,
    IReadOnlyList<Competition> competitions,
    IReadOnlyList<Competition> upcomingCompetitions,
    bool remindWeekend,
    ClubCatalog clubs,
    AnnouncedStore store,
    CardRenderer renderer,
    IPublisher publisher,
    bool ignoreAnnounced,
    bool saveState)
{
    private readonly Dictionary<(string Slug, DateOnly Date), IReadOnlyList<Match>> _cache = [];

    public async Task RunAsync(IReadOnlyList<DigestKind> kinds, DateOnly vietnamDate, CancellationToken ct)
    {
        var reference = ReportWindow.ReferenceFor(vietnamDate);
        var state = ignoreAnnounced ? new AnnouncedState() : store.Load();

        foreach (var kind in kinds)
        {
            var digest = kind == DigestKind.Results
                ? await BuildResultsAsync(state, reference, ct)
                : await BuildUpcomingAsync(state, reference, ct);

            Console.WriteLine($"{kind}: {digest.Matches.Count} trận, {digest.Updates.Count} cập nhật");
            if (digest.IsEmpty)
            {
                await publisher.NotifyAsync(EmptyMessage(kind), ct);
                continue;
            }

            var sections = await RenderCardsAsync(digest, ct);
            var drafts = new[]
            {
                new FacebookFormatter(clubs).Format(digest),
                new ThreadsFormatter(clubs).Format(digest),
                new XFormatter(clubs).Format(digest),
            };
            await publisher.PublishAsync(kind, DigestText.Header(digest, vietnamDate), sections, drafts, ct);

            // Gửi xong mới lưu: nếu gửi lỗi, lần chạy sau vẫn báo lại các trận này.
            state.MarkAnnounced(digest, DateTimeOffset.UtcNow);
            if (saveState)
                store.Save(state);
        }

        if (saveState)
        {
            state.Purge(DateTimeOffset.UtcNow);
            store.Save(state);
        }
    }

    // Trận đã báo được bỏ qua nên "mới": có thể vẫn còn trận, chỉ là đã báo ở lần trước.
    private static string EmptyMessage(DigestKind kind) => kind == DigestKind.Results
        ? "⚽ Không có kết quả trận đấu mới."
        : $"📅 Không có trận mới trong lịch {ReportWindow.UpcomingDays} ngày tới.";

    private async Task<Digest> BuildResultsAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ignoreAnnounced ? ReportWindow.AllRecentResults(reference) : ReportWindow.Results(reference);
        return state.BuildResults(await FetchAsync(competitions, window, ct), window);
    }

    private async Task<Digest> BuildUpcomingAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ReportWindow.Upcoming(reference);
        var matches = await FetchAsync(upcomingCompetitions, window, ct);
        IReadOnlyList<Match> weekend = remindWeekend
            ? (await FetchAsync(competitions, window, ct)).Where(m => ReportWindow.IsWeekend(m.KickoffUtc)).ToList()
            : [];
        var rechecked = await RecheckAsync(state, [.. matches, .. weekend], ct);
        return state.BuildUpcoming(matches, rechecked, window, weekend);
    }

    private async Task<IReadOnlyList<Match>> FetchAsync(IReadOnlyList<Competition> from, ReportWindow window, CancellationToken ct)
    {
        var all = new Dictionary<string, Match>();
        foreach (var competition in from)
        {
            foreach (var date in window.FetchDates())
            {
                if (!_cache.TryGetValue((competition.Slug, date), out var matches))
                {
                    matches = await source.GetMatchesAsync(competition, date, ct);
                    _cache[(competition.Slug, date)] = matches;
                }
                foreach (var match in matches)
                    all[match.Id] = match;
            }
        }
        return all.Values.ToList();
    }

    // Không lấy lại được một trận thì chỉ ghi log: không nên vì nó mà mất cả bài hôm đó.
    private async Task<IReadOnlyList<Match>> RecheckAsync(AnnouncedState state, IReadOnlyList<Match> fetched, CancellationToken ct)
    {
        var fetchedIds = fetched.Select(m => m.Id).ToHashSet();
        var bySlug = competitions.ToDictionary(c => c.Slug);
        var result = new List<Match>();
        foreach (var (matchId, slug) in state.PendingRechecks().Where(p => !fetchedIds.Contains(p.MatchId)))
        {
            if (!bySlug.TryGetValue(slug, out var competition))
                continue;
            try
            {
                if (await source.GetMatchAsync(competition, matchId, ct) is { } match)
                    result.Add(match);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Không kiểm tra lại được trận {matchId}: {ex.Message}");
            }
        }
        return result;
    }

    // Không vẽ được ảnh thì vẫn gửi bài chữ và báo cho chủ trang biết.
    private async Task<IReadOnlyList<CardSection>> RenderCardsAsync(Digest digest, CancellationToken ct)
    {
        try
        {
            return await renderer.RenderAsync(digest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await publisher.NotifyAsync($"⚠️ Không vẽ được ảnh {digest.Kind}: {ex.Message}", ct);
            return [];
        }
    }
}

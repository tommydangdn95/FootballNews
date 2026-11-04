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
    ClubCatalog clubs,
    AnnouncedStore store,
    CardRenderer renderer,
    IPublisher publisher,
    bool saveState,
    bool notifyWhenEmpty)
{
    private readonly Dictionary<(string Slug, DateOnly Date), IReadOnlyList<Match>> _cache = [];

    public async Task RunAsync(IReadOnlyList<DigestKind> kinds, DateOnly vietnamDate, CancellationToken ct)
    {
        var reference = ReportWindow.ReferenceFor(vietnamDate);
        var state = store.Load();
        var sentAny = false;

        foreach (var kind in kinds)
        {
            var digest = kind == DigestKind.Results
                ? await BuildResultsAsync(state, reference, ct)
                : await BuildUpcomingAsync(state, reference, ct);

            Console.WriteLine($"{kind}: {digest.Matches.Count} trận mới, {digest.Updates.Count} cập nhật");
            if (digest.IsEmpty)
                continue;

            var cards = await RenderCardsAsync(digest, ct);
            var drafts = new[]
            {
                new FacebookFormatter(clubs).Format(digest),
                new ThreadsFormatter(clubs).Format(digest),
                new XFormatter(clubs).Format(digest),
            };
            await publisher.PublishAsync(kind, cards, drafts, ct);
            sentAny = true;

            // Gửi xong mới lưu: nếu gửi lỗi, lần chạy sau vẫn báo lại các trận này.
            state.MarkAnnounced(digest, DateTimeOffset.UtcNow);
            if (saveState)
                store.Save(state);
        }

        if (!sentAny && notifyWhenEmpty)
            await publisher.NotifyAsync("Hôm nay không có trận ông lớn", ct);

        if (saveState)
        {
            state.Purge(DateTimeOffset.UtcNow);
            store.Save(state);
        }
    }

    private async Task<Digest> BuildResultsAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ReportWindow.Results(reference);
        return state.BuildResults(await FetchAsync(window, ct), window);
    }

    private async Task<Digest> BuildUpcomingAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ReportWindow.Upcoming(reference);
        var matches = await FetchAsync(window, ct);
        var rechecked = await RecheckAsync(state, matches, ct);
        return state.BuildUpcoming(matches, rechecked, window);
    }

    private async Task<IReadOnlyList<Match>> FetchAsync(ReportWindow window, CancellationToken ct)
    {
        var all = new Dictionary<string, Match>();
        foreach (var competition in competitions)
        {
            foreach (var date in window.FetchDates())
            {
                if (!_cache.TryGetValue((competition.Slug, date), out var matches))
                {
                    matches = await source.GetMatchesAsync(competition, date, ct);
                    _cache[(competition.Slug, date)] = matches;
                }
                foreach (var match in BigClubFilter.Apply(matches, clubs))
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
    private async Task<IReadOnlyList<byte[]>> RenderCardsAsync(Digest digest, CancellationToken ct)
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

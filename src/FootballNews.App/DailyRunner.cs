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
    AnnouncedStore store,
    CardRenderer renderer,
    IPublisher publisher,
    bool ignoreAnnounced,
    bool saveState)
{
    private readonly Dictionary<(string Slug, DateOnly Date), IReadOnlyList<Match>> _cache = [];
    private readonly Dictionary<(string Slug, string TeamId, int? Season), IReadOnlyList<TeamResult>?> _teamResults = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _gamesPlayed = [];

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
                await publisher.NotifyAsync(DigestText.Empty(kind, ignoreAnnounced), ct);
                continue;
            }

            var sections = await RenderCardsAsync(digest, ct);
            await publisher.PublishAsync(kind, DigestText.Header(digest, vietnamDate), sections, ct);

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

    private async Task<Digest> BuildResultsAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ignoreAnnounced ? ReportWindow.AllRecentResults(reference) : ReportWindow.Results(reference);
        var digest = state.BuildResults(await FetchAsync(competitions, window, ct), window);
        return digest with { Matches = await WithRoundsAsync(digest.Matches, ct) };
    }

    private async Task<Digest> BuildUpcomingAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ReportWindow.Upcoming(reference);
        var matches = await FetchAsync(upcomingCompetitions, window, ct);
        IReadOnlyList<Match> weekend = remindWeekend
            ? (await FetchAsync(competitions, window, ct)).Where(m => ReportWindow.IsWeekend(m.KickoffUtc)).ToList()
            : [];
        var rechecked = await RecheckAsync(state, [.. matches, .. weekend], ct);
        var digest = state.BuildUpcoming(matches, rechecked, window, weekend);
        return digest with
        {
            Matches = await WithRoundsAsync(await WithFormAsync(digest.Matches, ct), ct),
            Updates = await WithFormAsync(digest.Updates, ct),
        };
    }

    // Không lấy được bảng xếp hạng thì ảnh của giải đó chỉ không ghi vòng, bài vẫn gửi.
    private async Task<IReadOnlyList<Match>> WithRoundsAsync(IReadOnlyList<Match> matches, CancellationToken ct)
    {
        var withRounds = new Dictionary<string, Match>();
        foreach (var group in matches.Where(m => Matchday.IsNumbered(m.Stage)).GroupBy(m => m.Competition.Slug))
        {
            var competition = group.First().Competition;
            if (!_gamesPlayed.TryGetValue(competition.Slug, out var played))
            {
                try
                {
                    played = await source.GetGamesPlayedAsync(competition, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Không lấy được bảng xếp hạng {competition.Slug}: {ex.Message}");
                    played = new Dictionary<string, int>();
                }
                _gamesPlayed[competition.Slug] = played;
            }
            foreach (var m in Matchday.Assign(group.ToList(), played))
                withRounds[m.Id] = m;
        }
        return matches.Select(m => withRounds.GetValueOrDefault(m.Id, m)).ToList();
    }

    private async Task<IReadOnlyList<Match>> WithFormAsync(IReadOnlyList<Match> matches, CancellationToken ct)
    {
        var result = new List<Match>(matches.Count);
        foreach (var m in matches)
        {
            result.Add(m with
            {
                Home = m.Home with { Form = await FormAsync(m, m.Home.Team, ct) },
                Away = m.Away with { Form = await FormAsync(m, m.Away.Team, ct) },
            });
        }
        return result;
    }

    // Không lấy được lịch của một đội thì đội đó chỉ không có phong độ, bài vẫn gửi.
    private async Task<string?> FormAsync(Match match, Team team, CancellationToken ct)
    {
        var key = (match.Competition.Slug, team.Id, match.Season);
        if (!_teamResults.TryGetValue(key, out var results))
        {
            try
            {
                results = await source.GetTeamResultsAsync(match.Competition, team.Id, match.Season, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Không lấy được phong độ của {team.Name} ({match.Competition.Slug}): {ex.Message}");
                results = null;
            }
            _teamResults[key] = results;
        }
        return results is null ? null : TeamForm.Build(results, match.KickoffUtc);
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

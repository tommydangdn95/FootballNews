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
    private readonly Dictionary<string, Standings?> _standings = [];

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
            if (kind == DigestKind.Results)
                await PublishStandingsAsync(digest.Matches, vietnamDate, ct);

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
        var matches = await WithRoundsAsync(digest.Matches, ct);
        // Khung force 7 ngày có thể chứa cả vòng trước (vd trận tối thứ Hai), nên chỉ giữ vòng mới nhất.
        if (ignoreAnnounced)
            matches = Matchday.LatestRoundOnly(matches);
        return digest with { Matches = matches };
    }

    private async Task<Digest> BuildUpcomingAsync(AnnouncedState state, DateTimeOffset reference, CancellationToken ct)
    {
        var window = ReportWindow.Upcoming(reference);
        // Lấy rộng hơn khung để bài có đủ cả vòng của các trận trong khung, kể cả trận đá sau khung.
        var extended = window with { End = window.End.AddDays(ReportWindow.RoundCompletionDays) };
        // Đánh số vòng trên mọi trận lấy được: nếu chỉ đánh trên trận mới, đội có trận trước đó đã báo sẽ bị lệch vòng.
        var fetched = await WithRoundsAsync(await FetchAsync(upcomingCompetitions, extended, ct), ct);
        var matches = Matchday.CompleteRounds(fetched, window);
        IReadOnlyList<Match> weekend = remindWeekend
            ? (await WithRoundsAsync(await FetchAsync(competitions, window, ct), ct)).Where(m => ReportWindow.IsWeekend(m.KickoffUtc)).ToList()
            : [];
        var rechecked = await RecheckAsync(state, [.. matches, .. weekend], ct);
        var digest = state.BuildUpcoming(matches, rechecked, extended, weekend);
        return digest with
        {
            Matches = await WithFormAsync(digest.Matches, ct),
            Updates = await WithFormAsync(digest.Updates, ct),
        };
    }

    // Không lấy được bảng xếp hạng thì ảnh của giải đó chỉ không ghi vòng, bài vẫn gửi.
    private async Task<IReadOnlyList<Match>> WithRoundsAsync(IReadOnlyList<Match> matches, CancellationToken ct)
    {
        var withRounds = new Dictionary<string, Match>();
        foreach (var group in matches.Where(m => Matchday.IsNumbered(m.Stage)).GroupBy(m => m.Competition.Slug))
        {
            var played = (await StandingsAsync(group.First().Competition, ct))?.GamesPlayed() ?? new Dictionary<string, int>();
            foreach (var m in Matchday.Assign(group.ToList(), played))
                withRounds[m.Id] = m;
        }
        return matches.Select(m => withRounds.GetValueOrDefault(m.Id, m)).ToList();
    }

    /// <summary>
    /// Sau bài kết quả, gửi bảng xếp hạng của các giải có trận vừa đá (giải VĐQG, vòng phân hạng cúp châu Âu).
    /// Lỗi ở đây chỉ báo cho chủ trang: bài kết quả đã gửi xong.
    /// </summary>
    private async Task PublishStandingsAsync(IReadOnlyList<Match> results, DateOnly vietnamDate, CancellationToken ct)
    {
        var tables = new List<Standings>();
        foreach (var group in results.Where(m => Matchday.IsNumbered(m.Stage)).GroupBy(m => m.Competition.Slug).OrderBy(g => g.First().Competition.Order))
        {
            if (await StandingsAsync(group.First().Competition, ct) is not { Rows.Count: > 0 } table)
                continue;
            // Phong độ tính đến lúc chạy, cùng giải và cùng mùa với các trận vừa đá.
            var season = group.Select(m => m.Season).FirstOrDefault(s => s is not null);
            var rows = new List<StandingRow>(table.Rows.Count);
            foreach (var row in table.Rows)
                rows.Add(row with { Form = await FormAsync(table.Competition, row.Team, season, DateTimeOffset.UtcNow, ct) });
            tables.Add(table with { Rows = rows, Round = group.Max(m => m.Round) });
        }
        Console.WriteLine($"Standings: {tables.Count} giải");
        if (tables.Count == 0)
            return;

        try
        {
            var sections = await renderer.RenderStandingsAsync(tables);
            await publisher.PublishStandingsAsync(DigestText.StandingsHeader(tables, vietnamDate), sections, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await publisher.NotifyAsync($"⚠️ Không gửi được bảng xếp hạng: {ex.Message}", ct);
        }
    }

    // Lấy một lần mỗi giải trong một lần chạy, dùng chung cho đánh số vòng và ảnh bảng xếp hạng.
    private async Task<Standings?> StandingsAsync(Competition competition, CancellationToken ct)
    {
        if (_standings.TryGetValue(competition.Slug, out var cached))
            return cached;
        Standings? table;
        try
        {
            table = await source.GetStandingsAsync(competition, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Không lấy được bảng xếp hạng {competition.Slug}: {ex.Message}");
            table = null;
        }
        _standings[competition.Slug] = table;
        return table;
    }

    private async Task<IReadOnlyList<Match>> WithFormAsync(IReadOnlyList<Match> matches, CancellationToken ct)
    {
        var result = new List<Match>(matches.Count);
        foreach (var m in matches)
        {
            result.Add(m with
            {
                Home = m.Home with { Form = await FormAsync(m.Competition, m.Home.Team, m.Season, m.KickoffUtc, ct) },
                Away = m.Away with { Form = await FormAsync(m.Competition, m.Away.Team, m.Season, m.KickoffUtc, ct) },
            });
        }
        return result;
    }

    // Không lấy được lịch của một đội thì đội đó chỉ không có phong độ, bài vẫn gửi.
    /// <param name="before">Chỉ tính các trận đá trước mốc này: giờ bóng lăn với lịch đấu, lúc chạy với bảng xếp hạng.</param>
    private async Task<string?> FormAsync(Competition competition, Team team, int? season, DateTimeOffset before, CancellationToken ct)
    {
        var key = (competition.Slug, team.Id, season);
        if (!_teamResults.TryGetValue(key, out var results))
        {
            try
            {
                results = await source.GetTeamResultsAsync(competition, team.Id, season, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Không lấy được phong độ của {team.Name} ({competition.Slug}): {ex.Message}");
                results = null;
            }
            _teamResults[key] = results;
        }
        return results is null ? null : TeamForm.Build(results, before);
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

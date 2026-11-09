using System.Text.RegularExpressions;

namespace FootballNews.Core.Models;

/// <summary>
/// Số vòng đấu của giải VĐQG và vòng phân hạng cúp châu Âu. ESPN không ghi số vòng trong trận,
/// nên suy ra từ số trận đã đá của từng đội trên bảng xếp hạng lúc chạy.
/// </summary>
public static partial class Matchday
{
    /// <summary>Giai đoạn đánh số vòng: cả mùa giải VĐQG (slug là tên mùa) hoặc vòng phân hạng cúp châu Âu.</summary>
    public static bool IsNumbered(string? stage) =>
        stage is not null && (stage == "league-phase" || LeagueSeason().IsMatch(stage));

    /// <summary>
    /// Gán <see cref="Match.Round"/> cho các trận của một giải. <paramref name="gamesPlayed"/>: id đội → số trận đã đá.
    /// Trận đã đá: trận mới nhất của đội là vòng thứ (số trận đã đá), trận trước đó lùi dần.
    /// Trận sắp đá: trận gần nhất là vòng (số trận đã đá + 1), trận sau đó tăng dần.
    /// Đội đá bù hay còn trận hoãn có số trận lệch với cả giải, nên lấy số lớn hơn của hai đội.
    /// </summary>
    public static IReadOnlyList<Match> Assign(IReadOnlyList<Match> matches, IReadOnlyDictionary<string, int> gamesPlayed)
    {
        var rounds = new Dictionary<string, int>();
        foreach (var finished in new[] { true, false })
        {
            var pool = matches
                .Where(m => IsNumbered(m.Stage) && m.Status is not (MatchStatus.Postponed or MatchStatus.Cancelled))
                .Where(m => (m.Status == MatchStatus.Finished) == finished)
                .ToList();
            var teamIds = pool.SelectMany(m => new[] { m.Home.Team.Id, m.Away.Team.Id }).Distinct();
            foreach (var teamId in teamIds)
            {
                if (!gamesPlayed.TryGetValue(teamId, out var played))
                    continue;
                var own = pool.Where(m => m.Home.Team.Id == teamId || m.Away.Team.Id == teamId);
                own = finished ? own.OrderByDescending(m => m.KickoffUtc) : own.OrderBy(m => m.KickoffUtc);
                var i = 0;
                foreach (var m in own)
                {
                    var round = finished ? played - i : played + 1 + i;
                    i++;
                    if (round > 0 && (!rounds.TryGetValue(m.Id, out var current) || round > current))
                        rounds[m.Id] = round;
                }
            }
        }
        return matches.Select(m => rounds.TryGetValue(m.Id, out var r) ? m with { Round = r } : m).ToList();
    }

    /// <summary>
    /// Chỉ giữ vòng mới nhất của mỗi giải (dùng khi chạy force, để không lẫn trận của vòng trước).
    /// Trận không có số vòng (vòng loại trực tiếp, hoặc không lấy được bảng xếp hạng) được giữ nguyên.
    /// Trận đá bù của vòng cũ có số vòng nhỏ hơn nên bị bỏ.
    /// </summary>
    public static IReadOnlyList<Match> LatestRoundOnly(IReadOnlyList<Match> matches)
    {
        var latest = matches
            .Where(m => m.Round is not null)
            .GroupBy(m => (m.Competition.Slug, m.Stage))
            .ToDictionary(g => g.Key, g => g.Max(m => m.Round!.Value));
        return matches
            .Where(m => m.Round is null || m.Round == latest[(m.Competition.Slug, m.Stage)])
            .ToList();
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-")]
    private static partial Regex LeagueSeason();
}

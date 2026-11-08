using System.Globalization;
using FootballNews.Core.Models;

namespace FootballNews.Core.Live;

/// <summary>Một sự kiện mới phát hiện. Tỉ số là tỉ số ngay sau sự kiện.</summary>
public sealed record LiveEvent(
    LiveEventKind Kind,
    int HomeScore,
    int AwayScore,
    GoalRecord? Goal = null,
    string? TeamId = null,
    string? Player = null,
    string? Minute = null,
    bool ExtraTime = false,
    IReadOnlyList<MatchEvent>? Summary = null);

/// <summary>
/// So dữ liệu ESPN mới nhất với những gì đã thấy để tìm sự kiện mới: bàn thắng, VAR huỷ bàn, thẻ đỏ,
/// hết hiệp 1, hết 90 phút (có hiệp phụ), hết trận. Không báo gì về luân lưu.
/// </summary>
public static class LiveTracker
{
    /// <summary>Bắt đầu theo dõi: ghi nhận những gì đã xảy ra để không báo lại.</summary>
    public static WatchedMatch Start(Match match)
    {
        var watch = new WatchedMatch
        {
            MatchId = match.Id,
            Competition = match.Competition.Slug,
            KickoffUtc = match.KickoffUtc,
            Home = match.Home.Team,
            Away = match.Away.Team,
        };
        Update(watch, match);
        return watch;
    }

    /// <summary>Cập nhật <paramref name="watch"/> theo <paramref name="match"/> và trả các sự kiện mới, xếp theo thứ tự xảy ra.</summary>
    public static IReadOnlyList<LiveEvent> Update(WatchedMatch watch, Match match)
    {
        var events = new List<LiveEvent>();
        if (match.Status is MatchStatus.Scheduled or MatchStatus.Postponed or MatchStatus.Cancelled)
            return events;

        var homeId = watch.Home.Id;
        var awayId = watch.Away.Id;

        // Huỷ trước, rồi mới đến bàn mới: tỉ số chạy đúng khi cùng lúc có cả hai.
        var homeScore = match.Home.Score ?? 0;
        var awayScore = match.Away.Score ?? 0;
        var cancelled = CancelGoals(watch, match, homeId, homeScore).Concat(CancelGoals(watch, match, awayId, awayScore)).ToList();
        foreach (var goal in cancelled)
        {
            if (goal.TeamId == homeId) watch.HomeScore--; else watch.AwayScore--;
            events.Add(new LiveEvent(LiveEventKind.GoalCancelled, watch.HomeScore, watch.AwayScore, goal, goal.TeamId, goal.Player, goal.Minute));
        }

        var newGoals = NewGoals(watch, match, homeId, homeScore).Concat(NewGoals(watch, match, awayId, awayScore))
            .OrderBy(g => MinuteOrder(g.Minute))
            .ToList();
        var redCards = match.Events
            .Where(e => e.Type == MatchEventType.RedCard && !watch.RedCards.Contains(RedCardKey(e)))
            .ToList();

        // Bàn thắng và thẻ đỏ xen nhau theo phút.
        var timeline = newGoals.Select(g => (Order: MinuteOrder(g.Minute), Goal: (GoalRecord?)g, Card: (MatchEvent?)null))
            .Concat(redCards.Select(c => (Order: MinuteOrder(c.Minute), Goal: (GoalRecord?)null, Card: (MatchEvent?)c)))
            .OrderBy(x => x.Order);
        foreach (var (_, goal, card) in timeline)
        {
            if (goal is not null)
            {
                goal.Id = $"g{watch.NextGoalId++}";
                watch.Goals.Add(goal);
                if (goal.TeamId == homeId) watch.HomeScore++; else watch.AwayScore++;
                events.Add(new LiveEvent(LiveEventKind.Goal, watch.HomeScore, watch.AwayScore, goal, goal.TeamId, goal.Player, goal.Minute));
            }
            else
            {
                watch.RedCards.Add(RedCardKey(card!));
                events.Add(new LiveEvent(LiveEventKind.RedCard, watch.HomeScore, watch.AwayScore, null, card!.TeamId, card.Player, card.Minute));
            }
        }

        // Đồng bộ tỉ số với ESPN phòng khi danh sách sự kiện thiếu.
        watch.HomeScore = homeScore;
        watch.AwayScore = awayScore;

        var status = match.StatusName ?? "";
        var period = match.Period ?? 0;

        if (status == "STATUS_HALFTIME" && !watch.HalfTimeDone)
        {
            watch.HalfTimeDone = true;
            events.Add(new LiveEvent(LiveEventKind.HalfTime, watch.HomeScore, watch.AwayScore, Summary: Summary(watch, match)));
        }
        // Đã sang hiệp 2 mà chưa kịp thấy giờ nghỉ thì thôi, không báo muộn.
        if (period >= 2)
            watch.HalfTimeDone = true;

        if (!watch.EndOfRegulationDone && IsPastRegulation(status, period))
        {
            watch.EndOfRegulationDone = true;
            watch.HadExtraTime = true;
            var (home, away) = RegulationScore(watch);
            events.Add(new LiveEvent(LiveEventKind.EndOfRegulation, home, away, Summary: Summary(watch, match)));
        }

        if (!watch.FullTimeDone && (match.Status == MatchStatus.Finished || (watch.HadExtraTime && IsPastExtraTime(status, period))))
        {
            watch.FullTimeDone = true;
            events.Add(new LiveEvent(LiveEventKind.FullTime, watch.HomeScore, watch.AwayScore, ExtraTime: watch.HadExtraTime, Summary: Summary(watch, match)));
        }

        return events;
    }

    /// <summary>Thứ tự của phút: "45'+2'" đứng sau "45'" và trước "46'". Không đọc được thì xếp cuối.</summary>
    public static double MinuteOrder(string minute)
    {
        var parts = minute.Replace("'", "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var main))
            return double.MaxValue;
        var extra = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : 0;
        return main + extra / 100.0;
    }

    /// <summary>
    /// Bàn thắng và thẻ đỏ tính đến lúc này, xếp theo phút: dùng cho bài hết hiệp, hết trận.
    /// Bàn của mỗi đội lấy theo ESPN; danh sách ESPN thiếu so với tỉ số thì lấy các bàn bot đã ghi nhận.
    /// </summary>
    private static IReadOnlyList<MatchEvent> Summary(WatchedMatch watch, Match match)
    {
        var events = new List<MatchEvent>();
        foreach (var (teamId, score) in new[] { (watch.Home.Id, watch.HomeScore), (watch.Away.Id, watch.AwayScore) })
        {
            var espn = match.Events.Where(e => e.IsGoal && e.TeamId == teamId).ToList();
            events.AddRange(espn.Count >= score
                ? espn
                : watch.Goals.Where(g => g.TeamId == teamId && !g.Cancelled).Select(g => new MatchEvent(g.Type, g.Minute, g.Player, g.TeamId)));
        }
        events.AddRange(match.Events.Where(e => e.Type == MatchEventType.RedCard));
        return events.OrderBy(e => MinuteOrder(e.Minute)).ToList();
    }

    private static List<GoalRecord> CancelGoals(WatchedMatch watch, Match match, string teamId, int score)
    {
        var active = watch.Goals.Where(g => g.TeamId == teamId && !g.Cancelled).ToList();
        var count = active.Count - score;
        if (count <= 0)
            return [];

        // Ưu tiên bàn không còn trong danh sách sự kiện của ESPN, sau đó là bàn gần nhất.
        var current = match.Events.Where(e => e.IsGoal && e.TeamId == teamId).Select(e => (e.Player, e.Minute)).ToHashSet();
        var victims = active
            .OrderBy(g => current.Contains((g.Player, g.Minute)) ? 1 : 0)
            .ThenByDescending(g => MinuteOrder(g.Minute))
            .Take(count)
            .ToList();
        foreach (var goal in victims)
            goal.Cancelled = true;
        return victims;
    }

    private static List<GoalRecord> NewGoals(WatchedMatch watch, Match match, string teamId, int score)
    {
        var seen = watch.Goals.Count(g => g.TeamId == teamId && !g.Cancelled);
        var count = score - seen;
        if (count <= 0)
            return [];

        // Bàn mới là những bàn sau các bàn đã thấy. ESPN đôi khi chậm cập nhật danh sách sự kiện: thiếu thì để trống tên.
        var known = match.Events.Where(e => e.IsGoal && e.TeamId == teamId).ToList();
        return Enumerable.Range(seen, count)
            .Select(i => i < known.Count
                ? new GoalRecord { TeamId = teamId, Player = known[i].Player, Minute = known[i].Minute, Type = known[i].Type }
                : new GoalRecord { TeamId = teamId })
            .ToList();
    }

    private static string RedCardKey(MatchEvent card) =>
        card.Player.Length > 0 ? $"{card.TeamId}|{card.Player}" : $"{card.TeamId}|{card.Minute}";

    // ESPN đánh số hiệp: 1–2 chính thức, 3–4 hiệp phụ, 5 luân lưu.
    private static bool IsPastRegulation(string status, int period) =>
        period >= 3
        || status.Contains("END_OF_REGULATION")
        || status.Contains("EXTRA")
        || status.Contains("OVERTIME")
        || status.Contains("SHOOTOUT")
        || status.Contains("AET")
        || IsPenalties(status);

    private static bool IsPastExtraTime(string status, int period) =>
        period >= 5
        || status.Contains("END_OF_EXTRA")
        || status.Contains("SHOOTOUT")
        || IsPenalties(status);

    // "STATUS_FINAL_PEN". Không dùng Contains("PEN") vì "STATUS_SUSPENDED" cũng chứa chuỗi này.
    private static bool IsPenalties(string status) => status.EndsWith("_PEN", StringComparison.Ordinal);

    /// <summary>Tỉ số sau 90 phút: bỏ các bàn ghi ở hiệp phụ (phút chính lớn hơn 90).</summary>
    private static (int Home, int Away) RegulationScore(WatchedMatch watch)
    {
        var extra = watch.Goals.Where(g => !g.Cancelled && MinuteOrder(g.Minute) is >= 91 and < double.MaxValue).ToList();
        return (watch.HomeScore - extra.Count(g => g.TeamId == watch.Home.Id), watch.AwayScore - extra.Count(g => g.TeamId == watch.Away.Id));
    }
}

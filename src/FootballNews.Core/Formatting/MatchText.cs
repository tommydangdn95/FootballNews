using FootballNews.Core.Config;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Core.Formatting;

internal sealed class MatchText(ClubCatalog clubs)
{
    public string Title(DigestKind kind) => kind == DigestKind.Results
        ? "⚽ KẾT QUẢ ĐÊM QUA & RẠNG SÁNG NAY"
        : "📅 LỊCH THI ĐẤU SẮP TỚI";

    /// <summary>Kết quả: trận nổi bật trước. Lịch đấu trải nhiều ngày nên xếp theo giờ đá cho dễ đọc.</summary>
    public IEnumerable<Match> Ordered(Digest digest) => digest.Kind == DigestKind.Results
        ? clubs.ByHighlight(digest.Matches)
        : digest.Matches.OrderBy(m => m.KickoffUtc).ThenBy(m => m.Id, StringComparer.Ordinal);

    public string Name(MatchSide side) => clubs.DisplayName(side.Team);

    public string ScoreLine(Match m)
    {
        var line = $"{Name(m.Home)} {m.Home.Score}–{m.Away.Score} {Name(m.Away)}";
        return m.Home.ShootoutScore is { } home && m.Away.ShootoutScore is { } away
            ? $"{line} (pen {home}–{away})"
            : line;
    }

    public string Fixture(Match m) => $"{Name(m.Home)} vs {Name(m.Away)}";

    public string KickoffLine(Match m) => $"🕒 {m.KickoffUtc.ToOffset(ReportWindow.VietnamOffset):HH:mm dd/MM} | {Fixture(m)}";

    public string UpdateLine(Match m) => m.Status switch
    {
        MatchStatus.Postponed => $"⛔ HOÃN: {Fixture(m)}",
        MatchStatus.Cancelled => $"⛔ HUỶ: {Fixture(m)}",
        _ => $"🕒 Giờ mới {m.KickoffUtc.ToOffset(ReportWindow.VietnamOffset):HH:mm dd/MM} | {Fixture(m)}",
    };

    public static string GoalText(MatchEvent e) => e.Type switch
    {
        MatchEventType.PenaltyGoal => $"{e.Player} {e.Minute} (pen)",
        MatchEventType.OwnGoal => $"{e.Player} {e.Minute} (phản lưới)",
        _ => $"{e.Player} {e.Minute}",
    };

    public string? FormLine(Match m) =>
        string.IsNullOrEmpty(m.Home.Form) || string.IsNullOrEmpty(m.Away.Form)
            ? null
            : $"Phong độ: {m.Home.Form} | {m.Away.Form}";

    public string Hashtags(IEnumerable<Match> matches, int max)
    {
        var ordered = clubs.ByHighlight(matches).ToList();
        var tags = ordered.Select(m => m.Competition.Hashtag)
            .Concat(ordered.SelectMany(m => new[] { clubs.Hashtag(m.Home.Team), clubs.Hashtag(m.Away.Team) }))
            .OfType<string>()
            .Distinct()
            .Take(max);
        return string.Join(' ', tags.Select(t => "#" + t));
    }
}

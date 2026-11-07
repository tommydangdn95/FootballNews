namespace FootballNews.Core.Models;

public enum MatchStatus { Scheduled, Live, Finished, Postponed, Cancelled }

/// <param name="Form">Phong độ do <see cref="TeamForm"/> tính, xếp cũ đến mới. Chỉ có ở bài lịch đấu.</param>
public sealed record MatchSide(Team Team, int? Score, int? ShootoutScore, string? Form);

/// <param name="StatusName">Tên trạng thái gốc của ESPN, vd <c>STATUS_HALFTIME</c>. Dùng cho phần live.</param>
/// <param name="Period">Hiệp đang đá theo ESPN: 1–2 chính thức, 3–4 hiệp phụ, 5 luân lưu.</param>
public sealed record Match(
    string Id,
    Competition Competition,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    MatchSide Home,
    MatchSide Away,
    IReadOnlyList<MatchEvent> Events,
    string? Venue = null,
    int? Season = null,
    string? StatusName = null,
    int? Period = null);

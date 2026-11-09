namespace FootballNews.Core.Models;

public enum MatchStatus { Scheduled, Live, Finished, Postponed, Cancelled }

/// <param name="Form">Phong độ do <see cref="TeamForm"/> tính, xếp cũ đến mới. Chỉ có ở bài lịch đấu.</param>
public sealed record MatchSide(Team Team, int? Score, int? ShootoutScore, string? Form);

/// <param name="StatusName">Tên trạng thái gốc của ESPN, vd <c>STATUS_HALFTIME</c>. Dùng cho phần live.</param>
/// <param name="Period">Hiệp đang đá theo ESPN: 1–2 chính thức, 3–4 hiệp phụ, 5 luân lưu.</param>
/// <param name="Stage">Giai đoạn theo ESPN (<c>season.slug</c>): giải VĐQG là tên mùa, vd <c>2026-27-english-premier-league</c>;
/// cúp châu Âu là <c>league-phase</c>, <c>round-of-16</c>, <c>quarterfinals</c>... Chỉ có ở scoreboard.</param>
/// <param name="Leg">Lượt đi (1) hoặc lượt về (2) của trận đấu loại trực tiếp hai lượt.</param>
/// <param name="Round">Vòng đấu, do <see cref="Matchday"/> tính từ bảng xếp hạng. Chỉ có ở bài hằng ngày.</param>
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
    int? Period = null,
    string? Stage = null,
    int? Leg = null,
    int? Round = null);

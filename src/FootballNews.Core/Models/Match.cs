namespace FootballNews.Core.Models;

public enum MatchStatus { Scheduled, Live, Finished, Postponed, Cancelled }

/// <param name="Form">Phong độ do <see cref="TeamForm"/> tính, xếp cũ đến mới. Chỉ có ở bài lịch đấu.</param>
public sealed record MatchSide(Team Team, int? Score, int? ShootoutScore, string? Form);

public sealed record Match(
    string Id,
    Competition Competition,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    MatchSide Home,
    MatchSide Away,
    IReadOnlyList<MatchEvent> Events,
    string? Venue = null,
    int? Season = null);

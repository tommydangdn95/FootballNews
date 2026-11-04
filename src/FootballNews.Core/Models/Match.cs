namespace FootballNews.Core.Models;

public enum MatchStatus { Scheduled, Live, Finished, Postponed, Cancelled }

public sealed record MatchSide(Team Team, int? Score, int? ShootoutScore, string? Form);

public sealed record Match(
    string Id,
    Competition Competition,
    DateTimeOffset KickoffUtc,
    MatchStatus Status,
    MatchSide Home,
    MatchSide Away,
    IReadOnlyList<MatchEvent> Events);

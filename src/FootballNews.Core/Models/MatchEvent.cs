namespace FootballNews.Core.Models;

public enum MatchEventType { Goal, PenaltyGoal, OwnGoal, YellowCard, RedCard }

public sealed record MatchEvent(MatchEventType Type, string Minute, string Player, string TeamId)
{
    public bool IsGoal => Type is MatchEventType.Goal or MatchEventType.PenaltyGoal or MatchEventType.OwnGoal;
}

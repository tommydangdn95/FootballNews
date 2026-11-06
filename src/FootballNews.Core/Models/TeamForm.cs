namespace FootballNews.Core.Models;

/// <summary>Một trận đã đá của một đội: W thắng, D hoà, L thua.</summary>
public sealed record TeamResult(DateTimeOffset KickoffUtc, char Outcome);

/// <summary>Phong độ hiện trên ảnh lịch đấu: các trận gần nhất của đội trong cùng giải và cùng mùa.</summary>
public static class TeamForm
{
    public const int MaxMatches = 5;

    /// <summary>
    /// Tối đa 5 trận đá trước giờ bóng lăn, xếp cũ đến mới (trận gần nhất nằm cuối).
    /// Mùa mới đá ít trận thì trả ít hơn 5; chưa đá trận nào thì null.
    /// </summary>
    public static string? Build(IEnumerable<TeamResult> results, DateTimeOffset before)
    {
        var recent = results
            .Where(r => r.KickoffUtc < before)
            .OrderBy(r => r.KickoffUtc)
            .TakeLast(MaxMatches)
            .Select(r => r.Outcome)
            .ToArray();
        return recent.Length == 0 ? null : new string(recent);
    }
}

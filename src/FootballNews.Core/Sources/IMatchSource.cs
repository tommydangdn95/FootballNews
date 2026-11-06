using FootballNews.Core.Models;

namespace FootballNews.Core.Sources;

public interface IMatchSource
{
    Task<IReadOnlyList<Match>> GetMatchesAsync(Competition competition, DateOnly date, CancellationToken ct);

    /// <summary>Lấy lại một trận theo id. Dùng để phát hiện trận đã báo bị dời giờ hoặc hoãn.</summary>
    Task<Match?> GetMatchAsync(Competition competition, string matchId, CancellationToken ct);

    /// <summary>
    /// Các trận đã đá của một đội trong giải này. Có <paramref name="season"/> thì chỉ lấy mùa đó,
    /// không có thì lấy mùa hiện tại.
    /// </summary>
    Task<IReadOnlyList<TeamResult>> GetTeamResultsAsync(Competition competition, string teamId, int? season, CancellationToken ct);
}

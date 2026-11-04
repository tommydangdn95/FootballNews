using FootballNews.Core.Models;

namespace FootballNews.Core.Sources;

public interface IMatchSource
{
    Task<IReadOnlyList<Match>> GetMatchesAsync(Competition competition, DateOnly date, CancellationToken ct);

    /// <summary>Lấy lại một trận theo id. Dùng để phát hiện trận đã báo bị dời giờ hoặc hoãn.</summary>
    Task<Match?> GetMatchAsync(Competition competition, string matchId, CancellationToken ct);
}

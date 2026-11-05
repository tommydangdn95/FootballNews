using System.Text.Json;
using FootballNews.Core.Models;

namespace FootballNews.Core.Config;

public sealed record Club(string EspnId, string Name, int Priority, string Hashtag);

public sealed class ClubCatalog
{
    private const int NotBigClubPriority = 99;
    private readonly Dictionary<string, Club> _clubs;

    public ClubCatalog(IEnumerable<Club> clubs) => _clubs = clubs.ToDictionary(c => c.EspnId);

    public static ClubCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<ClubsFile>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException($"Không đọc được {path}");
        return new ClubCatalog(file.Clubs);
    }

    public bool IsBigClub(Team team) => _clubs.ContainsKey(team.Id);

    public string DisplayName(Team team) => _clubs.TryGetValue(team.Id, out var club) ? club.Name : team.Name;

    public string? Hashtag(Team team) => _clubs.GetValueOrDefault(team.Id)?.Hashtag;

    /// <summary>Thứ tự nổi bật: có đội priority cao trước, rồi trận có hai ông lớn trước.</summary>
    public IEnumerable<Match> ByHighlight(IEnumerable<Match> matches) => matches
        .OrderBy(m => Math.Min(Priority(m.Home.Team), Priority(m.Away.Team)))
        .ThenByDescending(m => (IsBigClub(m.Home.Team) ? 1 : 0) + (IsBigClub(m.Away.Team) ? 1 : 0))
        .ThenBy(m => m.KickoffUtc)
        .ThenBy(m => m.Id, StringComparer.Ordinal);

    private int Priority(Team team) => _clubs.GetValueOrDefault(team.Id)?.Priority ?? NotBigClubPriority;

    private sealed record ClubsFile(List<Club> Clubs);
}

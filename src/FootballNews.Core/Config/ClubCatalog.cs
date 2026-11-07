using System.Text.Json;
using FootballNews.Core.Models;

namespace FootballNews.Core.Config;

public sealed record Club(string EspnId, string Name, int Priority, string Hashtag);

public sealed class ClubCatalog
{
    private readonly Dictionary<string, Club> _clubs;

    public ClubCatalog(IEnumerable<Club> clubs) => _clubs = clubs.ToDictionary(c => c.EspnId);

    public static ClubCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<ClubsFile>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException($"Không đọc được {path}");
        return new ClubCatalog(file.Clubs);
    }

    public string DisplayName(Team team) => _clubs.TryGetValue(team.Id, out var club) ? club.Name : team.Name;

    /// <summary>Hashtag của đội, không có dấu #. Đội ngoài danh sách thì ghép tên ESPN, bỏ khoảng trắng và dấu.</summary>
    public string Hashtag(Team team) => _clubs.TryGetValue(team.Id, out var club) && club.Hashtag.Length > 0
        ? club.Hashtag
        : Formatting.Hashtags.FromName(team.Name);

    private sealed record ClubsFile(List<Club> Clubs);
}

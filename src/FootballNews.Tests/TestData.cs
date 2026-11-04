using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Tests;

internal static class TestData
{
    public static readonly string Root = FindRoot();
    public static readonly ClubCatalog Clubs = ClubCatalog.Load(Path.Combine(Root, "config", "clubs.json"));
    public static readonly IReadOnlyList<Competition> Competitions = CompetitionCatalog.Load(Path.Combine(Root, "config", "competitions.json"));

    public static Competition Competition(string slug) => Competitions.Single(c => c.Slug == slug);

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(Root, "tests", "fixtures", name));

    public static Match Match(
        string id,
        DateTimeOffset kickoff,
        MatchStatus status = MatchStatus.Scheduled,
        string homeId = "364",
        string homeName = "Liverpool",
        string awayId = "359",
        string awayName = "Arsenal",
        int? homeScore = null,
        int? awayScore = null) => new(
            id,
            Competition("eng.1"),
            kickoff,
            status,
            new MatchSide(new Team(homeId, homeName, null), homeScore, null, null),
            new MatchSide(new Team(awayId, awayName, null), awayScore, null, null),
            []);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config", "clubs.json")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc");
    }
}

using FootballNews.Core.Models;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class TeamFormTests
{
    private static readonly IReadOnlyList<TeamResult> Liverpool =
        EspnParser.ParseTeamSchedule(TestData.Fixture("schedule-eng1-364-2026.json"), "364", 2026);

    [Fact]
    public void Takes_last_five_oldest_first()
    {
        // Bournemouth W, Man City L, Brentford W, Brighton W, Arsenal L (01/11).
        Assert.Equal("WLWWL", TeamForm.Build(Liverpool, new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Shows_fewer_than_five_early_in_the_season()
    {
        // Newcastle D, Nottingham D, Ipswich W.
        Assert.Equal("DDW", TeamForm.Build(Liverpool, new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Ignores_matches_from_the_fixture_kickoff_onwards()
    {
        var kickoff = new DateTimeOffset(2026, 11, 1, 16, 30, 0, TimeSpan.Zero);
        Assert.EndsWith("W", TeamForm.Build(Liverpool, kickoff));
    }

    [Fact]
    public void Null_before_the_first_match()
    {
        Assert.Null(TeamForm.Build(Liverpool, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}

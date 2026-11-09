using FootballNews.Core.Models;

namespace FootballNews.Tests;

public class MatchdayTests
{
    private const string League = "2026-27-english-premier-league";
    private static readonly DateTimeOffset Saturday = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private static Match Game(string id, string home, string away, DateTimeOffset kickoff, MatchStatus status = MatchStatus.Scheduled, string? stage = League) =>
        TestData.Match(id, kickoff, status, homeId: home, awayId: away) with { Stage = stage };

    [Theory]
    [InlineData(League, true)]
    [InlineData("league-phase", true)]
    [InlineData("round-of-16", false)]
    [InlineData(null, false)]
    public void Only_league_seasons_and_league_phase_are_numbered(string? stage, bool expected) =>
        Assert.Equal(expected, Matchday.IsNumbered(stage));

    [Fact]
    public void Upcoming_match_is_the_round_after_games_played()
    {
        var matches = Matchday.Assign([Game("1", "a", "b", Saturday), Game("2", "a", "c", Saturday.AddDays(3))],
            new Dictionary<string, int> { ["a"] = 10, ["b"] = 10, ["c"] = 10 });

        Assert.Equal(11, matches[0].Round);
        Assert.Equal(12, matches[1].Round);
    }

    [Fact]
    public void Finished_match_is_the_round_equal_to_games_played()
    {
        var matches = Matchday.Assign([
            Game("1", "a", "b", Saturday, MatchStatus.Finished),
            Game("2", "c", "a", Saturday.AddDays(-3), MatchStatus.Finished),
        ], new Dictionary<string, int> { ["a"] = 10, ["b"] = 10, ["c"] = 9 });

        Assert.Equal(10, matches[0].Round);
        Assert.Equal(9, matches[1].Round);
    }

    [Fact]
    public void Team_with_a_game_in_hand_does_not_lower_the_round()
    {
        var match = Assert.Single(Matchday.Assign([Game("1", "a", "b", Saturday)], new Dictionary<string, int> { ["a"] = 9, ["b"] = 10 }));

        Assert.Equal(11, match.Round);
    }

    [Fact]
    public void Latest_round_only_drops_the_previous_round_of_each_competition()
    {
        var serieA = TestData.Competition("ita.1");
        var matches = Matchday.LatestRoundOnly([
            Game("1", "a", "b", Saturday.AddDays(-5), MatchStatus.Finished) with { Round = 9 },
            Game("2", "c", "d", Saturday, MatchStatus.Finished) with { Round = 10 },
            Game("3", "e", "f", Saturday, MatchStatus.Finished) with { Round = 10 },
            Game("4", "g", "h", Saturday.AddDays(-5), MatchStatus.Finished, "2026-27-italian-serie-a") with { Competition = serieA, Round = 11 },
        ]);

        Assert.Equal(new[] { "2", "3", "4" }, matches.Select(m => m.Id));
    }

    [Fact]
    public void Latest_round_only_keeps_matches_without_a_round()
    {
        var matches = Matchday.LatestRoundOnly([
            Game("1", "a", "b", Saturday, MatchStatus.Finished, stage: "round-of-16"),
            Game("2", "c", "d", Saturday, MatchStatus.Finished),
        ]);

        Assert.Equal(new[] { "1", "2" }, matches.Select(m => m.Id));
    }

    [Fact]
    public void Knockout_postponed_and_unknown_teams_get_no_round()
    {
        var matches = Matchday.Assign([
            Game("1", "a", "b", Saturday, stage: "round-of-16"),
            Game("2", "a", "b", Saturday, MatchStatus.Postponed),
            Game("3", "x", "y", Saturday),
        ], new Dictionary<string, int> { ["a"] = 4, ["b"] = 4 });

        Assert.All(matches, m => Assert.Null(m.Round));
    }
}

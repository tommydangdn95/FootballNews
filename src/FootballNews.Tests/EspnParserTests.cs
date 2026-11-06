using FootballNews.Core.Models;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class EspnParserTests
{
    [Fact]
    public void Reads_venue_when_present_and_tolerates_it_missing()
    {
        var match = EspnParser.ParseScoreboard(TestData.Fixture("eng1-20261101.json"), TestData.Competition("eng.1")).Single(m => m.Id == "401879246");
        Assert.Equal("Anfield", match.Venue);

        var summary = EspnParser.ParseSummary(TestData.Fixture("summary-401879246.json"), "401879246", TestData.Competition("eng.1"));
        Assert.Null(summary.Venue);
    }

    [Fact]
    public void Parses_finished_match_from_scoreboard()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("eng1-20261101.json"), TestData.Competition("eng.1"));

        var match = Assert.Single(matches, m => m.Id == "401879246");
        Assert.Equal(MatchStatus.Finished, match.Status);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 16, 30, 0, TimeSpan.Zero), match.KickoffUtc);
        Assert.Equal("Liverpool", match.Home.Team.Name);
        Assert.Equal(0, match.Home.Score);
        Assert.Equal("Arsenal", match.Away.Team.Name);
        Assert.Equal(1, match.Away.Score);
        Assert.Null(match.Home.Form);
        Assert.Equal(2026, match.Season);
        Assert.EndsWith("364.png", match.Home.Team.LogoUrl);

        var goal = Assert.Single(match.Events, e => e.IsGoal);
        Assert.Equal("Havertz", goal.Player);
        Assert.Equal("16'", goal.Minute);
        Assert.Equal("359", goal.TeamId);
        Assert.Equal(4, match.Events.Count(e => e.Type == MatchEventType.YellowCard && e.TeamId == "364"));
        Assert.Equal(4, match.Events.Count(e => e.Type == MatchEventType.YellowCard && e.TeamId == "359"));
    }

    [Fact]
    public void Scheduled_match_has_no_score()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("ucl-pre-20261104.json"), TestData.Competition("uefa.champions"));

        Assert.Equal(9, matches.Count);
        Assert.All(matches, m =>
        {
            Assert.Equal(MatchStatus.Scheduled, m.Status);
            Assert.Null(m.Home.Score);
            Assert.Null(m.Away.Score);
        });
    }

    [Fact]
    public void Parses_penalty_goal()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("ucl-post-20261103.json"), TestData.Competition("uefa.champions"));

        Assert.Equal(9, matches.Count);
        Assert.Contains(matches.SelectMany(m => m.Events), e => e.Type == MatchEventType.PenaltyGoal);
    }

    [Fact]
    public void Summary_endpoint_matches_scoreboard_shape()
    {
        var match = EspnParser.ParseSummary(TestData.Fixture("summary-401879246.json"), "401879246", TestData.Competition("eng.1"));

        Assert.Equal(MatchStatus.Finished, match.Status);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 16, 30, 0, TimeSpan.Zero), match.KickoffUtc);
        Assert.Equal("364", match.Home.Team.Id);
        Assert.Equal("359", match.Away.Team.Id);
        Assert.False(string.IsNullOrEmpty(match.Home.Team.Name));
    }

    [Theory]
    [InlineData("STATUS_POSTPONED", "post", "false", MatchStatus.Postponed)]
    [InlineData("STATUS_CANCELED", "post", "false", MatchStatus.Cancelled)]
    [InlineData("STATUS_FINAL_PEN", "post", "true", MatchStatus.Finished)]
    [InlineData("STATUS_SECOND_HALF", "in", "false", MatchStatus.Live)]
    public void Maps_status(string name, string state, string completed, MatchStatus expected)
    {
        var json = $$"""
            { "events": [ { "id": "1", "competitions": [ {
              "date": "2026-11-01T16:30Z",
              "status": { "type": { "name": "{{name}}", "state": "{{state}}", "completed": {{completed}} } },
              "competitors": [
                { "homeAway": "home", "score": "1", "team": { "id": "1", "displayName": "A" } },
                { "homeAway": "away", "score": "1", "team": { "id": "2", "displayName": "B" } }
              ] } ] } ] }
            """;

        var match = Assert.Single(EspnParser.ParseScoreboard(json, TestData.Competition("eng.1")));

        Assert.Equal(expected, match.Status);
    }

    [Fact]
    public void Shootout_kicks_are_not_goals()
    {
        var json = """
            { "events": [ { "id": "1", "competitions": [ {
              "date": "2026-11-01T16:30Z",
              "status": { "type": { "name": "STATUS_FINAL_PEN", "state": "post", "completed": true } },
              "competitors": [
                { "homeAway": "home", "score": "1", "shootoutScore": 4, "team": { "id": "1", "displayName": "A" } },
                { "homeAway": "away", "score": "1", "shootoutScore": 3, "team": { "id": "2", "displayName": "B" } }
              ],
              "details": [ { "scoringPlay": true, "shootout": true, "penaltyKick": true, "team": { "id": "1" } } ]
            } ] } ] }
            """;

        var match = Assert.Single(EspnParser.ParseScoreboard(json, TestData.Competition("eng.1")));

        Assert.Empty(match.Events);
        Assert.Equal(4, match.Home.ShootoutScore);
    }

    [Fact]
    public void Parses_team_schedule_into_results()
    {
        var results = EspnParser.ParseTeamSchedule(TestData.Fixture("schedule-eng1-364-2026.json"), "364", 2026);

        Assert.Equal(9, results.Count);
        var arsenal = Assert.Single(results, r => r.KickoffUtc.Date == new DateTime(2026, 11, 1));
        Assert.Equal('L', arsenal.Outcome);
        var newcastle = Assert.Single(results, r => r.KickoffUtc.Date == new DateTime(2026, 8, 23));
        Assert.Equal('D', newcastle.Outcome);
    }

    [Fact]
    public void Team_schedule_skips_other_seasons()
    {
        Assert.Empty(EspnParser.ParseTeamSchedule(TestData.Fixture("schedule-eng1-364-2026.json"), "364", 2025));
    }

    // Dựng theo chung kết C1 30/05/2026 (PSG 1–1 Arsenal, PSG thắng luân lưu): lịch đội không có shootoutScore.
    private const string ShootoutSchedule = """
        { "events": [
          { "date": "2026-05-30T16:00Z", "season": { "year": 2025 }, "competitions": [ {
            "status": { "type": { "state": "post", "completed": true, "name": "STATUS_FINAL_PEN" } },
            "competitors": [
              { "team": { "id": "160" }, "score": { "value": 1.0 }, "winner": true },
              { "team": { "id": "359" }, "score": { "value": 1.0 }, "winner": false } ] } ] },
          { "date": "2026-04-14T19:00Z", "season": { "year": 2025 }, "competitions": [ {
            "status": { "type": { "state": "post", "completed": true, "name": "STATUS_FULL_TIME" } },
            "competitors": [
              { "team": { "id": "160" }, "score": { "value": 1.0 }, "winner": true },
              { "team": { "id": "359" }, "score": { "value": 1.0 }, "winner": false } ] } ] },
          { "date": "2026-06-05T20:00Z", "season": { "year": 2025 }, "competitions": [ {
            "status": { "type": { "state": "post", "completed": false, "name": "STATUS_POSTPONED" } },
            "competitors": [
              { "team": { "id": "160" }, "score": { "value": 0.0 } },
              { "team": { "id": "359" }, "score": { "value": 0.0 } } ] } ] }
        ] }
        """;

    [Fact]
    public void Team_schedule_uses_final_result_after_shootout()
    {
        var psg = EspnParser.ParseTeamSchedule(ShootoutSchedule, "160", 2025).OrderBy(r => r.KickoffUtc).ToList();
        var arsenal = EspnParser.ParseTeamSchedule(ShootoutSchedule, "359", 2025).OrderBy(r => r.KickoffUtc).ToList();

        // Trận hoà thường vẫn là D dù có cờ winner (lượt về đi tiếp nhờ tổng tỉ số); trận hoãn bị bỏ.
        Assert.Equal("DW", new string(psg.Select(r => r.Outcome).ToArray()));
        Assert.Equal("DL", new string(arsenal.Select(r => r.Outcome).ToArray()));
    }

    [Fact]
    public void Team_schedule_reads_shootout_score_when_present()
    {
        const string json = """
            { "events": [ { "date": "2026-11-01T20:00Z", "season": { "year": 2026 }, "competitions": [ {
              "status": { "type": { "state": "post", "completed": true, "name": "STATUS_FINAL_PEN" } },
              "competitors": [
                { "team": { "id": "1" }, "score": { "value": 2.0 }, "shootoutScore": 3 },
                { "team": { "id": "2" }, "score": { "value": 2.0 }, "shootoutScore": 4 } ] } ] } ] }
            """;

        Assert.Equal('L', Assert.Single(EspnParser.ParseTeamSchedule(json, "1", 2026)).Outcome);
    }

    [Fact]
    public void Missing_events_fails_loudly()
    {
        Assert.Throws<FormatException>(() => EspnParser.ParseScoreboard("{}", TestData.Competition("eng.1")));
    }
}

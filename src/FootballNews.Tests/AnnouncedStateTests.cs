using FootballNews.Core.Dedup;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Tests;

public class AnnouncedStateTests
{
    private static readonly DateTimeOffset Reference = ReportWindow.ReferenceFor(new DateOnly(2026, 11, 4));
    private static readonly ReportWindow UpcomingWindow = ReportWindow.Upcoming(Reference);
    private static readonly ReportWindow ResultsWindow = ReportWindow.Results(Reference);
    private static readonly DateTimeOffset Kickoff = new(2026, 11, 4, 20, 0, 0, TimeSpan.Zero);

    private static AnnouncedState StateWithUpcoming(params string[] ids)
    {
        var state = new AnnouncedState();
        state.MarkAnnounced(new Digest(DigestKind.Upcoming, ids.Select(id => TestData.Match(id, Kickoff)).ToList(), []), Reference);
        return state;
    }

    [Fact]
    public void Result_is_announced_only_once()
    {
        var state = new AnnouncedState();
        var match = TestData.Match("1", Kickoff.AddDays(-1), MatchStatus.Finished, homeScore: 1, awayScore: 0);

        var first = state.BuildResults([match], ResultsWindow);
        state.MarkAnnounced(first, Reference);
        var second = state.BuildResults([match], ResultsWindow);

        Assert.Single(first.Matches);
        Assert.True(second.IsEmpty);
    }

    [Fact]
    public void Unfinished_matches_are_not_results()
    {
        var digest = new AnnouncedState().BuildResults(
            [TestData.Match("1", Kickoff.AddDays(-1), MatchStatus.Live), TestData.Match("2", Kickoff.AddDays(-1), MatchStatus.Postponed)],
            ResultsWindow);

        Assert.True(digest.IsEmpty);
    }

    [Fact]
    public void Same_kickoff_is_skipped()
    {
        Assert.True(StateWithUpcoming("1").BuildUpcoming([TestData.Match("1", Kickoff)], [], UpcomingWindow).IsEmpty);
    }

    [Fact]
    public void Changed_kickoff_becomes_update()
    {
        var digest = StateWithUpcoming("1").BuildUpcoming([TestData.Match("1", Kickoff.AddHours(1))], [], UpcomingWindow);

        Assert.Empty(digest.Matches);
        Assert.Single(digest.Updates);
    }

    [Fact]
    public void Match_moved_outside_window_is_caught_by_recheck()
    {
        var moved = TestData.Match("1", Kickoff.AddDays(5));

        var digest = StateWithUpcoming("1").BuildUpcoming([], [moved], UpcomingWindow);

        Assert.Equal(moved, Assert.Single(digest.Updates));
    }

    [Fact]
    public void Newly_postponed_match_in_window_is_reported_as_update()
    {
        var digest = new AnnouncedState().BuildUpcoming([TestData.Match("1", Kickoff, MatchStatus.Postponed)], [], UpcomingWindow);

        Assert.Empty(digest.Matches);
        Assert.Single(digest.Updates);
    }

    [Fact]
    public void Postponed_is_reported_once()
    {
        var state = StateWithUpcoming("1");
        var postponed = TestData.Match("1", Kickoff, MatchStatus.Postponed);

        var first = state.BuildUpcoming([], [postponed], UpcomingWindow);
        state.MarkAnnounced(first, Reference);
        var second = state.BuildUpcoming([], [postponed], UpcomingWindow);

        Assert.Single(first.Updates);
        Assert.True(second.IsEmpty);
    }

    [Fact]
    public void Finished_match_on_recheck_is_not_a_schedule_change()
    {
        var finished = TestData.Match("1", Kickoff, MatchStatus.Finished, homeScore: 1, awayScore: 1);

        Assert.True(StateWithUpcoming("1").BuildUpcoming([], [finished], UpcomingWindow).IsEmpty);
    }

    [Fact]
    public void Announced_result_is_not_rechecked()
    {
        var state = StateWithUpcoming("1", "2");
        state.MarkAnnounced(new Digest(DigestKind.Results, [TestData.Match("1", Kickoff, MatchStatus.Finished)], []), Reference);

        Assert.Equal("2", Assert.Single(state.PendingRechecks()).MatchId);
    }

    [Fact]
    public void Purge_removes_entries_older_than_30_days()
    {
        var state = new AnnouncedState();
        state.MarkAnnounced(new Digest(DigestKind.Upcoming, [TestData.Match("old", Kickoff)], []), Reference.AddDays(-31));
        state.MarkAnnounced(new Digest(DigestKind.Upcoming, [TestData.Match("new", Kickoff)], []), Reference);

        state.Purge(Reference);

        Assert.Equal("new", Assert.Single(state.Upcoming).Key);
    }

    [Fact]
    public void State_survives_save_and_load()
    {
        var path = Path.GetTempFileName();
        try
        {
            var state = StateWithUpcoming("1");
            state.MarkAnnounced(new Digest(DigestKind.Results, [TestData.Match("2", Kickoff)], []), Reference);

            var store = new AnnouncedStore(path);
            store.Save(state);
            var loaded = store.Load();

            Assert.Equal(Kickoff, loaded.Upcoming["1"].KickoffUtc);
            Assert.Equal(MatchStatus.Scheduled, loaded.Upcoming["1"].Status);
            Assert.Equal("eng.1", loaded.Upcoming["1"].Competition);
            Assert.True(loaded.Results.ContainsKey("2"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using FootballNews.Core.Filtering;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class WindowTests
{
    private static readonly DateTimeOffset Reference = ReportWindow.ReferenceFor(new DateOnly(2026, 11, 4));

    [Fact]
    public void Reference_is_8am_vietnam_time()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 1, 0, 0, TimeSpan.Zero), Reference);
    }

    [Fact]
    public void Results_include_last_night_but_not_the_night_before()
    {
        var window = ReportWindow.Results(Reference);

        Assert.True(window.Contains(new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero)));
        Assert.False(window.Contains(new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Upcoming_covers_next_24_hours()
    {
        var window = ReportWindow.Upcoming(Reference);

        Assert.True(window.Contains(Reference));
        Assert.True(window.Contains(new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero)));
        Assert.False(window.Contains(Reference.AddHours(24)));
    }

    [Fact]
    public void Fetch_dates_include_espn_us_date_margin()
    {
        DateOnly[] expected = [new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5)];

        Assert.Equal(expected, ReportWindow.Upcoming(Reference).FetchDates());
    }

    [Fact]
    public void Vietnam_today_rolls_over_at_17h_utc()
    {
        Assert.Equal(new DateOnly(2026, 11, 4), ReportWindow.VietnamToday(new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 3), ReportWindow.VietnamToday(new DateTimeOffset(2026, 11, 3, 16, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Filter_keeps_only_matches_with_a_big_club()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("ucl-pre-20261104.json"), TestData.Competition("uefa.champions"));

        var kept = BigClubFilter.Apply(matches, TestData.Clubs).ToList();

        // Không có ông lớn: Lens–Como, PSV–Club Brugge, Viking–Sabah.
        Assert.Equal(6, kept.Count);
        Assert.DoesNotContain(kept, m => m.Home.Team.Name == "Viking FK");
    }
}

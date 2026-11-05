using FootballNews.Core.Filtering;

namespace FootballNews.Tests;

public class WindowTests
{
    private static readonly DateTimeOffset Reference = ReportWindow.ReferenceFor(new DateOnly(2026, 11, 4));

    [Fact]
    public void Reference_is_7am_vietnam_time()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), Reference);
    }

    [Fact]
    public void Results_include_last_night_but_not_the_night_before()
    {
        var window = ReportWindow.Results(Reference);

        Assert.True(window.Contains(new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero)));
        Assert.False(window.Contains(new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Upcoming_runs_from_today_to_the_end_of_the_fifth_day_after()
    {
        var window = ReportWindow.Upcoming(Reference);
        var vietnam = ReportWindow.VietnamOffset;

        Assert.True(window.Contains(Reference));
        Assert.False(window.Contains(Reference.AddMinutes(-1)));
        Assert.True(window.Contains(new DateTimeOffset(2026, 11, 9, 23, 59, 0, vietnam)));
        Assert.False(window.Contains(new DateTimeOffset(2026, 11, 10, 0, 0, 0, vietnam)));
    }

    [Theory]
    [InlineData("uefa.champions", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)]
    [InlineData("uefa.europa", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)]
    [InlineData("uefa.europa.conf", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)]
    [InlineData("eng.1", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)]
    [InlineData("esp.1", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)]
    [InlineData("ger.1", DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday)]
    [InlineData("ita.1", DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday)]
    [InlineData("fra.1", DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday)]
    public void Upcoming_is_sent_on_the_competition_days(string slug, params DayOfWeek[] expected)
    {
        var competition = TestData.Competition(slug);

        // 01/11/2026 là Chủ nhật.
        var days = Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 11, 1).AddDays(i));

        Assert.Equal(expected, days.Where(competition.IsUpcomingOn).Select(d => d.DayOfWeek));
    }

    [Fact]
    public void Every_kickoff_of_every_competition_is_covered_by_an_earlier_upcoming_run()
    {
        foreach (var competition in TestData.Competitions)
        {
            var runs = Enumerable.Range(0, 28)
                .Select(i => new DateOnly(2026, 11, 2).AddDays(i))
                .Where(competition.IsUpcomingOn)
                .Select(d => ReportWindow.Upcoming(ReportWindow.ReferenceFor(d)))
                .ToList();

            // Bỏ tuần đầu vì chưa có lần chạy trước đó; kiểm tra từng 15 phút trong các tuần sau.
            var start = ReportWindow.ReferenceFor(new DateOnly(2026, 11, 9));
            for (var kickoff = start; kickoff < start.AddDays(14); kickoff = kickoff.AddMinutes(15))
                Assert.Contains(runs, run => run.Contains(kickoff));
        }
    }

    [Fact]
    public void All_recent_results_cover_a_week_and_the_rest_of_today()
    {
        var window = ReportWindow.AllRecentResults(Reference);

        Assert.True(window.Contains(Reference.AddDays(-6)));
        Assert.True(window.Contains(Reference.AddHours(14)));
        Assert.False(window.Contains(Reference.AddDays(-8)));
    }

    [Fact]
    public void Weekend_reminder_is_on_friday_and_uses_vietnam_date()
    {
        Assert.True(ReportWindow.IsWeekendReminderDay(new DateOnly(2026, 11, 6)));
        Assert.False(ReportWindow.IsWeekendReminderDay(new DateOnly(2026, 11, 7)));

        // 18:00 UTC thứ 6 đã là 01:00 thứ 7 ở Việt Nam.
        Assert.True(ReportWindow.IsWeekend(new DateTimeOffset(2026, 11, 6, 18, 0, 0, TimeSpan.Zero)));
        Assert.False(ReportWindow.IsWeekend(new DateTimeOffset(2026, 11, 6, 16, 59, 0, TimeSpan.Zero)));
        Assert.False(ReportWindow.IsWeekend(new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Fetch_dates_include_espn_us_date_margin()
    {
        var dates = ReportWindow.Upcoming(Reference).FetchDates();

        Assert.Equal(new DateOnly(2026, 11, 3), dates[0]);
        Assert.Equal(new DateOnly(2026, 11, 9), dates[^1]);
    }

    [Fact]
    public void Vietnam_today_rolls_over_at_17h_utc()
    {
        Assert.Equal(new DateOnly(2026, 11, 4), ReportWindow.VietnamToday(new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 3), ReportWindow.VietnamToday(new DateTimeOffset(2026, 11, 3, 16, 59, 0, TimeSpan.Zero)));
    }
}

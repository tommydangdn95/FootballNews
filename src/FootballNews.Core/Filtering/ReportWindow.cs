namespace FootballNews.Core.Filtering;

public sealed record ReportWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    // ESPN không có giờ kết thúc nên khung kết quả tính theo giờ đá, lùi thêm 3 giờ cho thời gian trận đấu.
    // Trận đã báo được announced.json loại ra.
    private static readonly TimeSpan ResultsLookback = TimeSpan.FromHours(27);

    // Ngày trong tham số dates của ESPN theo giờ Mỹ (chậm hơn UTC tối đa 8 giờ).
    private static readonly TimeSpan EspnDateLag = TimeSpan.FromHours(8);

    /// <summary>Lịch đấu tính từ hôm nay đến hết ngày thứ 5 sau đó.</summary>
    public const int UpcomingDays = 5;

    /// <summary>Bài lịch thứ 6 nhắc lại mọi trận thứ 7 và Chủ nhật, kể cả trận đã báo.</summary>
    public static bool IsWeekendReminderDay(DateOnly vietnamDate) => vietnamDate.DayOfWeek == DayOfWeek.Friday;

    public static bool IsWeekend(DateTimeOffset kickoff) =>
        kickoff.ToOffset(VietnamOffset).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Mốc báo cáo của một ngày: 07:00 giờ Việt Nam.</summary>
    public static DateTimeOffset ReferenceFor(DateOnly vietnamDate) =>
        new(vietnamDate.ToDateTime(new TimeOnly(7, 0)), VietnamOffset);

    public static DateOnly VietnamToday(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime);

    public static ReportWindow Results(DateTimeOffset reference) => new(reference - ResultsLookback, reference);

    public static ReportWindow Upcoming(DateTimeOffset reference) =>
        new(reference, new DateTimeOffset(reference.ToOffset(VietnamOffset).Date.AddDays(UpcomingDays + 1), VietnamOffset));

    public bool Contains(DateTimeOffset time) => time >= Start && time < End;

    public IReadOnlyList<DateOnly> FetchDates()
    {
        var first = DateOnly.FromDateTime((Start - EspnDateLag).UtcDateTime);
        var last = DateOnly.FromDateTime(End.UtcDateTime);
        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
            dates.Add(date);
        return dates;
    }
}

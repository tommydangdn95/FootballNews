namespace FootballNews.Core.Formatting;

public static class VietnameseCalendar
{
    private static readonly string[] Weekdays = ["Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];

    public static string Weekday(DateOnly date) => Weekdays[(int)date.DayOfWeek];
}

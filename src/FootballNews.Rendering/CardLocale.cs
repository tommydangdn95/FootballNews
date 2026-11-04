using System.Globalization;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>Chữ, múi giờ và cách viết ngày của ảnh theo từng ngôn ngữ.</summary>
public sealed record CardLocale(
    string Code,
    Func<DateTimeOffset, DateTimeOffset> ToLocalTime,
    string ResultsTitle,
    string UpcomingTitle,
    string ResultsSubtitle,
    string UpcomingSubtitle,
    string UpdatesHeading,
    string Postponed,
    string Cancelled,
    string NewTime,
    string OwnGoal,
    Func<Competition, string> CompetitionName,
    Func<DateOnly, string> ShortDate,
    Func<DateOnly, string> YearDate,
    Func<DateOnly, string> Weekday)
{
    private static readonly string[] VietnameseWeekdays = ["Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];

    // Giờ Anh đổi theo mùa (GMT/BST) nên phải dùng múi giờ thật, không dùng độ lệch cố định.
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static readonly CardLocale Vietnamese = new(
        Code: "vi",
        ToLocalTime: t => t.ToOffset(ReportWindow.VietnamOffset),
        ResultsTitle: "KẾT QUẢ",
        UpcomingTitle: "LỊCH THI ĐẤU",
        ResultsSubtitle: "Đêm qua & rạng sáng nay",
        UpcomingSubtitle: "Tối nay & rạng sáng mai",
        UpdatesHeading: "CẬP NHẬT",
        Postponed: "HOÃN",
        Cancelled: "HUỶ",
        NewTime: "giờ mới",
        OwnGoal: "phản lưới",
        CompetitionName: c => c.Name,
        ShortDate: d => d.ToString("dd/MM", CultureInfo.InvariantCulture),
        YearDate: d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        Weekday: d => VietnameseWeekdays[(int)d.DayOfWeek]);

    public static readonly CardLocale English = new(
        Code: "en",
        ToLocalTime: t => TimeZoneInfo.ConvertTime(t, UkTimeZone),
        ResultsTitle: "RESULTS",
        UpcomingTitle: "FIXTURES",
        ResultsSubtitle: "Full-time scores",
        UpcomingSubtitle: "Kick-off times in UK time",
        UpdatesHeading: "UPDATES",
        Postponed: "POSTPONED",
        Cancelled: "CANCELLED",
        NewTime: "new time",
        OwnGoal: "o.g.",
        CompetitionName: c => c.EnglishName,
        ShortDate: d => d.ToString("d MMM", CultureInfo.InvariantCulture),
        YearDate: d => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        Weekday: d => d.ToString("dddd", CultureInfo.InvariantCulture));

    /// <summary>Các ngôn ngữ cần vẽ, theo thứ tự gửi đi.</summary>
    public static readonly IReadOnlyList<CardLocale> All = [Vietnamese, English];
}

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
    string RecentResultsSubtitle,
    string UpcomingSubtitle,
    string UpdatesHeading,
    string Postponed,
    string Cancelled,
    string NewTime,
    string OwnGoal,
    Func<Competition, string> CompetitionName,
    Func<DateOnly, string> ShortDate,
    Func<Competition?, string> FormNote)
{
    // Giờ Anh đổi theo mùa (GMT/BST) nên phải dùng múi giờ thật, không dùng độ lệch cố định.
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static readonly CardLocale Vietnamese = new(
        Code: "vi",
        ToLocalTime: t => t.ToOffset(ReportWindow.VietnamOffset),
        ResultsTitle: "KẾT QUẢ",
        UpcomingTitle: "LỊCH THI ĐẤU",
        ResultsSubtitle: "Đêm qua & rạng sáng nay",
        RecentResultsSubtitle: "Các trận gần đây",
        UpcomingSubtitle: "Các trận sắp diễn ra",
        UpdatesHeading: "CẬP NHẬT",
        Postponed: "HOÃN",
        Cancelled: "HUỶ",
        NewTime: "giờ mới",
        OwnGoal: "phản lưới",
        CompetitionName: c => c.Name,
        ShortDate: d => d.ToString("dd/MM", CultureInfo.InvariantCulture),
        FormNote: c => $"Phong độ {TeamForm.MaxMatches} trận gần nhất tại {(c is null ? "giải của từng trận" : TitleCase(c.Name))} mùa này, trận mới nhất bên phải");

    public static readonly CardLocale English = new(
        Code: "en",
        ToLocalTime: t => TimeZoneInfo.ConvertTime(t, UkTimeZone),
        ResultsTitle: "RESULTS",
        UpcomingTitle: "FIXTURES",
        ResultsSubtitle: "Last night & this morning",
        RecentResultsSubtitle: "Recent full-time scores",
        UpcomingSubtitle: "Kick-off times in UK time",
        UpdatesHeading: "UPDATES",
        Postponed: "POSTPONED",
        Cancelled: "CANCELLED",
        NewTime: "new time",
        OwnGoal: "o.g.",
        CompetitionName: c => c.EnglishName,
        ShortDate: d => d.ToString("d MMM", CultureInfo.InvariantCulture),
        FormNote: c => c is null
            ? $"Form: last {TeamForm.MaxMatches} matches in each fixture's competition this season, most recent on the right"
            : $"Form: last {TeamForm.MaxMatches} {TitleCase(c.EnglishName)} matches this season, most recent on the right");

    // Tên giải trong config viết hoa (dùng cho tiêu đề); câu chú thích cần viết thường.
    private static string TitleCase(string name) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

    /// <summary>Các ngôn ngữ cần vẽ, theo thứ tự gửi đi.</summary>
    public static readonly IReadOnlyList<CardLocale> All = [Vietnamese, English];
}

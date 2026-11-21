using System.Globalization;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>Chữ, múi giờ và cách viết ngày của ảnh theo từng ngôn ngữ.</summary>
public sealed record CardLocale(
    string Code,
    Func<DateTimeOffset, Competition, DateTimeOffset> ToLocalTime,
    string ResultsTitle,
    string UpcomingTitle,
    Func<IReadOnlyCollection<Competition>, string?> TimeZoneNote,
    Func<int, int?, string> Round,
    Func<string, string?> Stage,
    Func<int, string> Leg,
    string UpdatesHeading,
    string Postponed,
    string Cancelled,
    string NewTime,
    string OwnGoal,
    Func<Competition, string> CompetitionName,
    Func<DateOnly, string> ShortDate,
    Func<Competition?, string> FormNote,
    string StandingsTitle,
    IReadOnlyList<string> StandingsColumns,
    Func<int, string> StandingsRound,
    Func<string, string> Zone)
{
    // Giờ châu Âu đổi theo mùa nên phải dùng múi giờ thật, không dùng độ lệch cố định.
    private static readonly (TimeZoneInfo Zone, string Label) UkTime = (TimeZoneInfo.FindSystemTimeZoneById("Europe/London"), "UK time");

    // Ảnh tiếng Anh ghi giờ của nước chủ nhà với giải quốc nội; giải khác (Anh, cúp châu Âu) dùng giờ Anh.
    private static readonly Dictionary<string, (TimeZoneInfo Zone, string Label)> LeagueTime = new()
    {
        ["esp.1"] = (TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"), "Spanish time"),
        ["ger.1"] = (TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), "German time"),
        ["fra.1"] = (TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), "French time"),
        ["ita.1"] = (TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"), "Italian time"),
    };

    private static (TimeZoneInfo Zone, string Label) EnglishTime(Competition c) =>
        LeagueTime.TryGetValue(c.Slug, out var time) ? time : UkTime;

    public static readonly CardLocale Vietnamese = new(
        Code: "vi",
        ToLocalTime: (t, _) => t.ToOffset(ReportWindow.VietnamOffset),
        ResultsTitle: "KẾT QUẢ",
        UpcomingTitle: "LỊCH THI ĐẤU",
        TimeZoneNote: _ => null,
        Round: (n, total) => total is { } t ? $"LƯỢT TRẬN {n}/{t}" : $"LƯỢT TRẬN {n}",
        Stage: stage => stage switch
        {
            "knockout-round-playoffs" => "VÒNG PLAY-OFF",
            "round-of-16" => "VÒNG 1/8",
            "quarterfinals" => "TỨ KẾT",
            "semifinals" => "BÁN KẾT",
            "final" => "CHUNG KẾT",
            _ => null,
        },
        Leg: leg => leg == 1 ? "LƯỢT ĐI" : "LƯỢT VỀ",
        UpdatesHeading: "CẬP NHẬT",
        Postponed: "HOÃN",
        Cancelled: "HUỶ",
        NewTime: "giờ mới",
        OwnGoal: "phản lưới",
        CompetitionName: c => c.Name,
        ShortDate: d => d.ToString("dd/MM", CultureInfo.InvariantCulture),
        FormNote: c => $"Phong độ {TeamForm.MaxMatches} trận gần nhất tại {(c is null ? "giải của từng trận" : TitleCase(c.Name))} mùa này, trận mới nhất bên phải",
        StandingsTitle: "BẢNG XẾP HẠNG",
        // Hạng, đội, trận, thắng, hoà, bại, hiệu số, điểm, phong độ.
        StandingsColumns: ["HẠNG", "ĐỘI", "TR", "T", "H", "B", "HS", "Đ", "PHONG ĐỘ"],
        StandingsRound: n => $"VÒNG {n}",
        Zone: zone => zone.ToLowerInvariant() switch
        {
            "champions league" => "Champions League",
            "champions league qualifying" => "Vòng loại Champions League",
            "europa league" => "Europa League",
            "europa league qualifying" => "Vòng loại Europa League",
            "conference league" => "Conference League",
            "conference league qualifying" => "Vòng loại Conference League",
            "relegation" or "relegated" => "Xuống hạng",
            "relegation playoff" => "Play-off trụ hạng",
            "promotion" => "Lên hạng",
            "qualifies for round of 16" => "Vào thẳng vòng 1/8",
            "knockout phase playoffs - seeded" => "Play-off (nhóm hạt giống)",
            "knockout phase playoffs - unseeded" => "Play-off (nhóm không hạt giống)",
            "eliminated" => "Bị loại",
            _ => zone,
        });

    public static readonly CardLocale English = new(
        Code: "en",
        ToLocalTime: (t, c) => TimeZoneInfo.ConvertTime(t, EnglishTime(c).Zone),
        ResultsTitle: "RESULTS",
        UpcomingTitle: "FIXTURES",
        TimeZoneNote: cs => cs.Select(c => EnglishTime(c).Label).Distinct().ToList() is [var label]
            ? $"Kick-off times in {label}"
            : "Kick-off times in local time",
        Round: (n, total) => total is { } t ? $"MATCHDAY {n} OF {t}" : $"MATCHDAY {n}",
        Stage: stage => stage switch
        {
            "knockout-round-playoffs" => "KNOCKOUT PLAY-OFFS",
            "round-of-16" => "ROUND OF 16",
            "quarterfinals" => "QUARTER-FINALS",
            "semifinals" => "SEMI-FINALS",
            "final" => "FINAL",
            _ => null,
        },
        Leg: leg => leg == 1 ? "1ST LEG" : "2ND LEG",
        UpdatesHeading: "UPDATES",
        Postponed: "POSTPONED",
        Cancelled: "CANCELLED",
        NewTime: "new time",
        OwnGoal: "o.g.",
        CompetitionName: c => c.EnglishName,
        ShortDate: d => d.ToString("d MMM", CultureInfo.InvariantCulture),
        FormNote: c => c is null
            ? $"Form: last {TeamForm.MaxMatches} matches in each fixture's competition this season, most recent on the right"
            : $"Form: last {TeamForm.MaxMatches} {TitleCase(c.EnglishName)} matches this season, most recent on the right",
        StandingsTitle: "STANDINGS",
        StandingsColumns: ["RANK", "TEAMS", "PLD", "WON", "DRA", "LOS", "GD", "PTS", "FORM"],
        StandingsRound: n => $"MATCHDAY {n}",
        Zone: zone => zone.ToLowerInvariant() switch
        {
            "relegated" => "Relegation",
            "knockout phase playoffs - seeded" => "Knockout play-offs (seeded)",
            "knockout phase playoffs - unseeded" => "Knockout play-offs (unseeded)",
            "qualifies for round of 16" => "Round of 16",
            _ => zone.Length > 0 ? char.ToUpperInvariant(zone[0]) + zone[1..] : zone,
        });

    // Tên giải trong config viết hoa (dùng cho tiêu đề); câu chú thích cần viết thường.
    private static string TitleCase(string name) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

    /// <summary>Các ngôn ngữ cần vẽ, theo thứ tự gửi đi.</summary>
    public static readonly IReadOnlyList<CardLocale> All = [Vietnamese, English];
}

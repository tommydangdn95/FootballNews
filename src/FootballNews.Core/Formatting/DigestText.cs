using System.Globalization;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Core.Formatting;

/// <summary>Các tin ngăn cách trên Telegram: mở đầu mỗi phần và trước ảnh từng giải.</summary>
public static class DigestText
{
    private const string HeavyRule = "━━━━━━━━━━━━━━━━━━";
    private const string LightRule = "──────────────────";
    private const string UpdatesLabel = "CẬP NHẬT";

    /// <summary>
    /// Tin khi không có trận, ghi rõ chế độ chạy. Chạy thường bỏ qua trận đã báo nên chỉ nói "mới":
    /// có thể vẫn còn trận, chỉ là đã báo ở lần trước.
    /// </summary>
    public static string Empty(DigestKind kind, bool force)
    {
        if (force)
        {
            return kind == DigestKind.Results
                ? $"⚽ Không có kết quả trận đấu nào trong {ReportWindow.AllRecentResultsDays} ngày qua. (Chạy force)"
                : $"📅 Không có trận nào trong lịch {ReportWindow.UpcomingDays} ngày tới. (Chạy force)";
        }

        return kind == DigestKind.Results
            ? "⚽ Không có kết quả trận đấu mới. (Chạy thường, không bật force)"
            : $"📅 Không có trận mới trong lịch {ReportWindow.UpcomingDays} ngày tới. (Chạy thường, không bật force)";
    }

    public static string Header(Digest digest, DateOnly vietnamDate)
    {
        var groups = ByCompetition(digest.Matches);
        var lines = new List<string>
        {
            HeavyRule,
            digest.Kind == DigestKind.Results ? "⚽ KẾT QUẢ THI ĐẤU" : "📅 LỊCH THI ĐẤU SẮP TỚI",
            $"Gửi lúc: {VietnameseCalendar.Weekday(vietnamDate)}, {vietnamDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            $"Các trận: {DateRange(digest.Matches.Count > 0 ? digest.Matches : digest.Updates)}",
            $"Tổng: {digest.Matches.Count} trận · {groups.Count} giải",
            "",
        };
        if (digest.Updates.Count > 0)
            lines.Add($"⚠️ Cập nhật: {digest.Updates.Count} trận");
        lines.AddRange(groups.Select(g => $"{g[0].Competition.Emoji} {TitleCase(g[0].Competition.Name)}: {g.Count} trận"));
        lines.Add(HeavyRule);
        return string.Join('\n', lines);
    }

    /// <param name="pageCount">Số ảnh của mỗi bản ngôn ngữ.</param>
    public static string Section(IReadOnlyList<Match> updates, IReadOnlyList<Match> matches, int pageCount)
    {
        var groups = ByCompetition(matches);
        var titles = groups.Select(g => $"{g[0].Competition.Emoji} {g[0].Competition.Name}").ToList();
        var counts = groups.Select(g => g.Count).ToList();
        if (updates.Count > 0)
        {
            titles.Insert(0, $"⚠️ {UpdatesLabel}");
            counts.Insert(0, updates.Count);
        }

        return $"{string.Join(" + ", titles)}\n{string.Join(" + ", counts)} trận · {DateRange([.. updates, .. matches])} · {pageCount} ảnh\n{LightRule}";
    }

    private static List<List<Match>> ByCompetition(IEnumerable<Match> matches) => matches
        .GroupBy(m => m.Competition.Slug)
        .Select(g => g.ToList())
        .OrderBy(g => g[0].Competition.Order)
        .ToList();

    private static string DateRange(IEnumerable<Match> matches)
    {
        var dates = matches
            .Select(m => DateOnly.FromDateTime(m.KickoffUtc.ToOffset(ReportWindow.VietnamOffset).DateTime))
            .Order()
            .ToList();
        if (dates.Count == 0)
            return "";
        var first = dates[0].ToString("dd/MM", CultureInfo.InvariantCulture);
        var last = dates[^1].ToString("dd/MM", CultureInfo.InvariantCulture);
        return first == last ? first : $"{first} – {last}";
    }

    private static string TitleCase(string name) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
}

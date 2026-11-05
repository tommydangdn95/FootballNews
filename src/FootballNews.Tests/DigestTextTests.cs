using FootballNews.Core.Formatting;
using FootballNews.Core.Models;

namespace FootballNews.Tests;

public class DigestTextTests
{
    // 03:00 ngày 07/11/2026 và 03:00 ngày 09/11/2026 giờ Việt Nam.
    private static readonly DateTimeOffset Early = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = new(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);

    private static Match In(string slug, string id, DateTimeOffset kickoff) =>
        TestData.Match(id, kickoff) with { Competition = TestData.Competition(slug) };

    [Fact]
    public void Header_lists_date_range_totals_and_each_competition()
    {
        var digest = new Digest(DigestKind.Upcoming, [In("esp.1", "1", Late), In("eng.1", "2", Early), In("eng.1", "3", Late)], []);

        var text = DigestText.Header(digest, new DateOnly(2026, 11, 6));

        Assert.Equal(
            """
            ━━━━━━━━━━━━━━━━━━
            📅 LỊCH THI ĐẤU SẮP TỚI
            Gửi lúc: Thứ Sáu, 06/11/2026
            Các trận: 07/11 – 09/11
            Tổng: 3 trận · 2 giải

            🏴 Ngoại Hạng Anh: 2 trận
            🇪🇸 La Liga: 1 trận
            ━━━━━━━━━━━━━━━━━━
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void Header_shows_results_title_and_updates()
    {
        var digest = new Digest(DigestKind.Results, [In("ita.1", "1", Early)], [In("ita.1", "2", Early)]);

        var text = DigestText.Header(digest, new DateOnly(2026, 11, 7));

        Assert.Contains("⚽ KẾT QUẢ THI ĐẤU", text);
        Assert.Contains("Gửi lúc: Thứ Bảy, 07/11/2026", text);
        Assert.Contains("Các trận: 07/11\n", text);
        Assert.Contains("⚠️ Cập nhật: 1 trận", text);
        Assert.Contains("🇮🇹 Serie A: 1 trận", text);
    }

    [Fact]
    public void Section_shows_competition_count_dates_and_pages()
    {
        var text = DigestText.Section([], [In("eng.1", "1", Early), In("eng.1", "2", Late)], 2);

        Assert.Equal("🏴 NGOẠI HẠNG ANH\n2 trận · 07/11 – 09/11 · 2 ảnh\n──────────────────", text);
    }

    [Fact]
    public void Section_names_every_competition_sharing_the_images()
    {
        var text = DigestText.Section([], [In("uefa.europa", "3", Early), In("uefa.champions", "1", Early), In("uefa.champions", "2", Early)], 1);

        Assert.StartsWith("⭐ CHAMPIONS LEAGUE + 🟠 EUROPA LEAGUE\n2 + 1 trận · 07/11 · 1 ảnh", text);
    }

    [Fact]
    public void Section_lists_updates_first()
    {
        var text = DigestText.Section([In("eng.1", "1", Early)], [In("eng.1", "2", Early)], 1);

        Assert.StartsWith("⚠️ CẬP NHẬT + 🏴 NGOẠI HẠNG ANH\n1 + 1 trận", text);
    }
}

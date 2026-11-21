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

    [Theory]
    [InlineData(DigestKind.Results, false, "⚽ Không có kết quả trận đấu mới. (Chạy thường, không bật force)")]
    [InlineData(DigestKind.Upcoming, false, "📅 Không có trận mới trong lịch 5 ngày tới. (Chạy thường, không bật force)")]
    [InlineData(DigestKind.Results, true, "⚽ Không có kết quả trận đấu nào trong 7 ngày qua. (Chạy force)")]
    [InlineData(DigestKind.Upcoming, true, "📅 Không có trận nào trong lịch 5 ngày tới. (Chạy force)")]
    public void Empty_message_states_the_run_mode(DigestKind kind, bool force, string expected)
    {
        Assert.Equal(expected, DigestText.Empty(kind, force));
    }

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

    private static Standings Table(string slug, int teams, int? round) => new(
        TestData.Competition(slug),
        Enumerable.Range(1, teams).Select(i => new StandingRow(i, new Team(i.ToString(), $"Team {i}", null), 10, 5, 3, 2, 15, 10, 18, null)).ToList(),
        round);

    [Fact]
    public void Standings_header_lists_each_competition_with_its_round()
    {
        var text = DigestText.StandingsHeader([Table("eng.1", 20, 12), Table("uefa.champions", 36, null)], new DateOnly(2026, 11, 6));

        Assert.Contains("📊 BẢNG XẾP HẠNG", text);
        Assert.Contains("Cập nhật sau kết quả: 2 giải", text);
        Assert.Contains("🏴 Ngoại Hạng Anh · Vòng 12", text);
        Assert.Contains("⭐ Champions League\n", text);
    }

    [Fact]
    public void Standings_section_states_round_teams_and_pages()
    {
        Assert.StartsWith("🏴 NGOẠI HẠNG ANH\nVòng 12 · 20 đội · 1 ảnh\n", DigestText.StandingsSection(Table("eng.1", 20, 12), 1));
        Assert.StartsWith("⭐ CHAMPIONS LEAGUE\n36 đội · 2 ảnh\n", DigestText.StandingsSection(Table("uefa.champions", 36, null), 2));
    }
}

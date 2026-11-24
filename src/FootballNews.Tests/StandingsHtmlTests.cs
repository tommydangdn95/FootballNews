using System.Net;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class StandingsHtmlTests
{
    private static readonly CardTheme Theme = new("Trang", "#000", "#111", "#222", "#333", "#444", "#fff", "#eee", "#ccc");
    private static readonly StandingsHtml Html = new(TestData.Clubs, Theme, "", "");

    private static Standings PremierLeague(int? round = 11) =>
        EspnParser.ParseStandings(TestData.Fixture("standings-eng1.json"), TestData.Competition("eng.1")) with { Round = round };

    private static Standings LeaguePhase(int teams) => new(
        TestData.Competition("uefa.champions"),
        Enumerable.Range(1, teams).Select(i => new StandingRow(i, new Team(i.ToString(), $"Club {i}", null), 5, 3, 1, 1, 9, 5, 10, i <= 8 ? "Qualifies for round of 16" : null)).ToList());

    private static int Rows(string page) => page.Split("<span class=\"pos\">").Length - 2;

    [Fact]
    public void League_of_twenty_fits_one_page_with_every_team()
    {
        var page = WebUtility.HtmlDecode(Assert.Single(Html.BuildPages(PremierLeague(), CardLocale.Vietnamese)));

        Assert.Contains("BẢNG XẾP HẠNG · VÒNG 11", page);
        Assert.DoesNotContain("11/38", page);
        Assert.Contains("500-dark/23.png", page);
        Assert.Contains("NGOẠI HẠNG ANH", page);
        Assert.DoesNotContain("09/11/2026", page);
        Assert.Equal(20, Rows(page));
        Assert.Contains("<span class=\"pts\">27</span>", page);
        Assert.Contains("<span class=\"num\">+10</span>", page);
        Assert.Contains("359.png", page);
    }

    [Fact]
    public void Rank_changes_are_worked_out_by_taking_the_new_results_back_out()
    {
        StandingRow Row(int rank, string id, int points, int gf, int ga) => new(rank, new Team(id, id, null), 10, 0, 0, 0, gf, ga, points, null);
        var table = new Standings(TestData.Competition("fra.1"), [Row(1, "A", 12, 10, 5), Row(2, "B", 11, 9, 6), Row(3, "C", 10, 8, 7), Row(4, "D", 9, 6, 6)]);
        // A thắng D 2-0: trước trận A có 9 điểm, hiệu số +3, D có 9 điểm, hiệu số +2 → A thứ 3, D thứ 4.
        var win = TestData.Match("1", DateTimeOffset.UnixEpoch, MatchStatus.Finished, "A", "A", "D", "D", 2, 0);

        var changes = table.RankChanges([win]);

        Assert.Equal(2, changes["A"]);
        Assert.Equal(-1, changes["B"]);
        Assert.Equal(-1, changes["C"]);
        Assert.Equal(0, changes["D"]);
        Assert.All(table.RankChanges([win with { Status = MatchStatus.Scheduled }]).Values, c => Assert.Equal(0, c));
    }

    [Fact]
    public void After_results_highlights_only_on_a_normal_run_and_falls_back_to_games_played_for_the_round()
    {
        StandingRow Row(int rank, string id, int points, int gf, int ga, int played = 10) => new(rank, new Team(id, id, null), played, 0, 0, 0, gf, ga, points, null);
        var table = new Standings(TestData.Competition("fra.1"), [Row(1, "A", 12, 10, 5, 11), Row(2, "B", 11, 9, 6), Row(3, "C", 10, 8, 7), Row(4, "D", 9, 6, 6, 11)]);
        var win = TestData.Match("1", DateTimeOffset.UnixEpoch, MatchStatus.Finished, "A", "A", "D", "D", 2, 0) with { Round = 11 };

        var normal = table.AfterResults([win], highlightPlayed: true);
        Assert.Equal(["A", "D"], normal.Rows.Where(r => r.JustPlayed).Select(r => r.Team.Id));
        Assert.Equal(2, normal.Rows[0].RankChange);
        Assert.Equal(11, normal.Round);

        // Chạy force: cả vòng vừa đá nên không tô, mũi tên vẫn giữ.
        var force = table.AfterResults([win], highlightPlayed: false);
        Assert.DoesNotContain(force.Rows, r => r.JustPlayed);
        Assert.Equal(2, force.Rows[0].RankChange);

        // Giải không có trận trong bài (vd C1 giữa hai lượt khi chạy force): không mũi tên, vòng là số trận đã đá nhiều nhất.
        var idle = table.AfterResults([], highlightPlayed: false);
        Assert.All(idle.Rows, r => Assert.Equal(0, r.RankChange));
        Assert.Equal(11, idle.Round);
    }

    [Fact]
    public void Rank_change_draws_a_green_up_or_red_down_triangle()
    {
        var table = PremierLeague();
        table = table with { Rows = table.Rows.Select(r => r with { RankChange = r.Rank switch { 1 => 2, 2 => -1, _ => 0 } }).ToList() };

        var page = Assert.Single(Html.BuildPages(table, CardLocale.Vietnamese));

        Assert.Contains("<span class=\"pos\">1<i class=\"move up\"></i></span>", page);
        Assert.Contains("<span class=\"pos\">2<i class=\"move down\"></i></span>", page);
        Assert.Contains("<span class=\"pos\">3</span>", page);
    }

    [Fact]
    public void Teams_that_just_played_are_highlighted_without_a_legend_entry()
    {
        var table = PremierLeague();
        table = table with { Rows = table.Rows.Select(r => r with { JustPlayed = r.Rank is 1 or 7 }).ToList() };

        var vi = WebUtility.HtmlDecode(Assert.Single(Html.BuildPages(table, CardLocale.Vietnamese)));
        Assert.Equal(2, vi.Split("<div class=\"row played\">").Length - 1);
        Assert.DoesNotContain("Vừa thi đấu", vi);

        var none = WebUtility.HtmlDecode(Assert.Single(Html.BuildPages(PremierLeague(), CardLocale.English)));
        Assert.DoesNotContain("row played", none);
    }

    [Fact]
    public void Zones_get_a_colour_bar_and_a_translated_legend()
    {
        var vi = WebUtility.HtmlDecode(Html.BuildPages(PremierLeague(), CardLocale.Vietnamese)[0]);
        // Hạng 1–4 dự Champions League chung một dải: bắt đầu ở hạng 1, kết thúc ở hạng 4.
        Assert.Contains($"<i class=\"zone start\" style=\"background: {StandingsHtml.ZoneColor("Champions League")}\">", vi);
        Assert.Equal(2, vi.Split($"<i class=\"zone\" style=\"background: {StandingsHtml.ZoneColor("Champions League")}\">").Length - 1);
        // Hạng 5 dự Europa League một mình: dải vừa bắt đầu vừa kết thúc.
        Assert.Contains($"<i class=\"zone start end\" style=\"background: {StandingsHtml.ZoneColor("Europa League")}\">", vi);
        Assert.Contains("Champions League", vi);
        Assert.DoesNotContain("Dự ", vi);
        Assert.Contains("Xuống hạng", vi);

        var en = WebUtility.HtmlDecode(Html.BuildPages(PremierLeague(), CardLocale.English)[0]);
        Assert.Contains("STANDINGS", en);
        Assert.Contains("PREMIER LEAGUE", en);
        Assert.Contains(">Relegation<", en);
        Assert.Contains("<span class=\"pts\">PTS</span>", en);
        Assert.Contains("STANDINGS · MATCHDAY 11", en);
    }

    [Fact]
    public void League_phase_of_thirty_six_splits_evenly_and_keeps_ranks()
    {
        var pages = Html.BuildPages(LeaguePhase(36), CardLocale.Vietnamese).Select(p => WebUtility.HtmlDecode(p)).ToList();

        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.Equal(18, Rows(p)));
        Assert.Contains(">19</span>", pages[1]);
        Assert.Contains("Vào thẳng vòng 1/8", pages[0]);
        Assert.DoesNotContain("Vào thẳng vòng 1/8", pages[1]);
    }

    [Fact]
    public void Missing_round_omits_the_round_heading()
    {
        var page = Html.BuildPages(PremierLeague(round: null), CardLocale.Vietnamese)[0];

        Assert.DoesNotContain("V&#210;NG", page);
        Assert.Contains("<p>B", page);
    }

    private static StandingRow Ranked(int rank, string? zone) => new(rank, new Team(rank.ToString(), $"Club {rank}", null), 12, 4, 4, 4, 10, 10, 16, zone);

    [Fact]
    public void Zones_not_earned_by_position_are_dropped()
    {
        // Như La Liga: hạng 10 mang "Europa League" (suất vô địch cúp) dù hạng 8–9 không có vùng nào.
        var rows = Enumerable.Range(1, 20).Select(r => Ranked(r, r switch
        {
            <= 5 => "Champions League",
            6 => "Europa League",
            7 => "Conference League qualifying",
            10 => "Europa League",
            >= 18 => "Relegation",
            _ => null,
        })).ToList();

        var zones = Standings.PositionalZones(rows).Select(r => r.Zone).ToList();

        Assert.Equal("Conference League qualifying", zones[6]);
        Assert.Null(zones[9]);
        Assert.Equal(["Relegation", "Relegation", "Relegation"], zones[17..]);
    }

    [Fact]
    public void Relegation_zone_must_reach_the_bottom()
    {
        var rows = new[] { Ranked(1, "Champions League"), Ranked(2, "Relegation"), Ranked(3, null) };

        Assert.Equal(["Champions League", null, null], Standings.PositionalZones(rows).Select(r => r.Zone));
    }

    [Fact]
    public void Form_column_shows_last_matches_with_a_note()
    {
        var table = PremierLeague();
        table = table with { Rows = [table.Rows[0] with { Form = "WWDLW" }, .. table.Rows.Skip(1)] };

        var page = WebUtility.HtmlDecode(Html.BuildPages(table, CardLocale.Vietnamese)[0]);

        Assert.Contains("<span class=\"form\">PHONG ĐỘ</span>", page);
        Assert.Contains("<span class=\"form\"><b class=\"w\">W</b><b class=\"w\">W</b><b class=\"d\">D</b><b class=\"l\">L</b><b class=\"w\">W</b></span>", page);
        Assert.Contains("Phong độ 5 trận gần nhất tại Ngoại Hạng Anh mùa này, trận mới nhất bên phải", page);
    }
}

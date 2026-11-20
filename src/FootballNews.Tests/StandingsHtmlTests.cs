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

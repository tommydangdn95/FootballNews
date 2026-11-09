using System.Net;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class CardHtmlTests
{
    private static readonly CardTheme Theme = new("Trang <test>", "#000", "#111", "#222", "#333", "#444", "#fff", "#eee", "#ccc");
    private static readonly CardHtml Html = new(TestData.Clubs, Theme, "", "");

    // 03:00 ngày 05/11/2026 và 19:30 ngày 05/11/2026 giờ Việt Nam.
    private static readonly DateTimeOffset Night = new(2026, 11, 4, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SameDayEvening = new(2026, 11, 5, 12, 30, 0, TimeSpan.Zero);

    // 19:30 ngày 04/11/2026 giờ Việt Nam.
    private static readonly DateTimeOffset DayBeforeEvening = new(2026, 11, 4, 12, 30, 0, TimeSpan.Zero);

    private static Digest BuildDigest(string fixture, string slug, DigestKind kind)
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture(fixture), TestData.Competition(slug));
        return new Digest(kind, matches, []);
    }

    private static Digest Upcoming(params DateTimeOffset[] kickoffs) => new(
        DigestKind.Upcoming,
        kickoffs.Select((k, i) => TestData.Match(i.ToString(), k)).ToList(),
        []);

    private static string SinglePage(Digest digest, CardLocale locale) => Html.BuildPages(digest, locale).Single();

    [Fact]
    public void Results_page_has_logo_score_and_scorer()
    {
        var digest = BuildDigest("eng1-20261101.json", "eng.1", DigestKind.Results);
        var other = TestData.Match("2", Night, MatchStatus.Finished, homeScore: 1, awayScore: 1);

        var page = Html.BuildPages(digest with { Matches = [.. digest.Matches, other] }, CardLocale.Vietnamese)
            .Single(p => p.Contains("Havertz"));

        Assert.Contains("364.png", page);
        Assert.Contains("0–1", page);
        Assert.Contains("NGOẠI HẠNG ANH", page);
    }

    [Fact]
    public void Upcoming_page_shows_form_in_order_with_note()
    {
        var match = TestData.Match("1", Night);
        match = match with { Home = match.Home with { Form = "WDL" }, Away = match.Away with { Form = "LLWWW" } };
        var digest = new Digest(DigestKind.Upcoming, [match], []);

        var vi = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains("<b class=\"w\">W</b><b class=\"d\">D</b><b class=\"l\">L</b>", vi);
        Assert.Contains("Phong độ 5 trận gần nhất tại Ngoại Hạng Anh mùa này, trận mới nhất bên phải", WebUtility.HtmlDecode(vi));

        var en = SinglePage(digest, CardLocale.English);
        Assert.Contains("Form: last 5 Premier League matches this season, most recent on the right", WebUtility.HtmlDecode(en));
    }

    [Fact]
    public void Form_note_is_hidden_without_form_and_on_results()
    {
        Assert.DoesNotContain("class=\"note\"", SinglePage(Upcoming(Night), CardLocale.Vietnamese));

        var finished = TestData.Match("1", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0);
        finished = finished with { Home = finished.Home with { Form = "W" } };
        Assert.DoesNotContain("class=\"note\"", SinglePage(new Digest(DigestKind.Results, [finished], []), CardLocale.Vietnamese));
    }

    [Fact]
    public void Upcoming_page_shows_vietnam_time()
    {
        var pages = Html.BuildPages(BuildDigest("ucl-pre-20261104.json", "uefa.champions", DigestKind.Upcoming), CardLocale.Vietnamese);

        Assert.Contains("00:45", string.Concat(pages));
        Assert.Contains("03:00", string.Concat(pages));
    }

    [Fact]
    public void Upcoming_page_has_no_date_heading()
    {
        var digest = Upcoming(DayBeforeEvening, SameDayEvening);

        Assert.All(CardLocale.All, locale =>
        {
            var page = SinglePage(digest, locale);
            Assert.DoesNotContain("<div class=\"date\">", page);
            Assert.DoesNotContain("–", page);
        });
    }

    [Fact]
    public void Each_language_uses_its_own_time_zone_for_time_and_date()
    {
        var digest = Upcoming(Night, Night);

        var vietnamese = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains(">03:00<", vietnamese);
        Assert.Contains("<div class=\"pen\">05/11</div>", vietnamese);

        var english = SinglePage(digest, CardLocale.English);
        Assert.Contains(">20:00<", english);
        Assert.Contains("<div class=\"pen\">4 Nov</div>", english);
    }

    [Fact]
    public void English_time_follows_british_summer_time()
    {
        var summer = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);
        var page = SinglePage(Upcoming(summer, summer), CardLocale.English);

        Assert.Contains(">19:00<", page);
    }

    [Theory]
    [InlineData("esp.1", "Spanish time")]
    [InlineData("ger.1", "German time")]
    [InlineData("fra.1", "French time")]
    public void English_page_uses_the_home_country_time_for_spain_germany_and_france(string slug, string label)
    {
        // 20:00 UTC ngày 04/11 là 21:00 giờ Tây Ban Nha/Đức/Pháp (CET).
        var match = TestData.Match("1", Night) with { Competition = TestData.Competition(slug) };
        var digest = new Digest(DigestKind.Upcoming, [match], []);

        var english = SinglePage(digest, CardLocale.English);
        Assert.Contains(">21:00<", english);
        Assert.Contains("<div class=\"pen\">4 Nov</div>", english);
        Assert.Contains($"Kick-off times in {label}", english);

        var vietnamese = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains(">03:00<", vietnamese);
        Assert.Contains("<div class=\"pen\">05/11</div>", vietnamese);
    }

    [Fact]
    public void Home_country_time_follows_central_european_summer_time()
    {
        var summer = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);
        var match = TestData.Match("1", summer) with { Competition = TestData.Competition("esp.1") };

        Assert.Contains(">20:00<", SinglePage(new Digest(DigestKind.Upcoming, [match], []), CardLocale.English));
    }

    [Fact]
    public void Results_in_english_use_the_home_country_time()
    {
        var match = TestData.Match("1", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0) with { Competition = TestData.Competition("ger.1") };

        Assert.Contains("<div class=\"pen\">4 Nov · 21:00</div>", SinglePage(new Digest(DigestKind.Results, [match], []), CardLocale.English));
    }

    [Fact]
    public void Single_day_still_shows_the_date_under_every_match()
    {
        var page = SinglePage(Upcoming(Night, SameDayEvening), CardLocale.Vietnamese);

        Assert.Equal(2, page.Split("<div class=\"pen\">05/11</div>").Length - 1);
    }

    [Fact]
    public void Several_days_show_per_match_date()
    {
        var digest = Upcoming(DayBeforeEvening, SameDayEvening);

        var vietnamese = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains("<div class=\"pen\">04/11</div>", vietnamese);
        Assert.Contains("<div class=\"pen\">05/11</div>", vietnamese);

        var english = SinglePage(digest, CardLocale.English);
        Assert.Contains("<div class=\"pen\">5 Nov</div>", english);
    }

    [Fact]
    public void Date_uses_vietnam_time_not_utc()
    {
        // 20:00 ngày 04/11 theo UTC đã là ngày 05/11 ở Việt Nam.
        var page = SinglePage(Upcoming(Night, Night), CardLocale.Vietnamese);

        Assert.Contains("<div class=\"pen\">05/11</div>", page);
        Assert.DoesNotContain("04/11", page);
    }

    [Fact]
    public void English_page_uses_english_labels_and_states_the_time_zone()
    {
        var page = SinglePage(Upcoming(Night, Night), CardLocale.English);

        Assert.Contains("FIXTURES", page);
        Assert.Contains("PREMIER LEAGUE", page);
        Assert.Contains("UK time", page);
        Assert.Contains("lang=\"en\"", page);
        Assert.DoesNotContain("LỊCH THI ĐẤU", page);
        Assert.DoesNotContain("NGOẠI HẠNG ANH", page);
    }

    [Fact]
    public void English_page_translates_update_status()
    {
        var digest = new Digest(DigestKind.Upcoming, [], [TestData.Match("1", Night, MatchStatus.Postponed), TestData.Match("2", Night, MatchStatus.Postponed)]);

        var page = SinglePage(digest, CardLocale.English);

        Assert.Contains("UPDATES", page);
        Assert.Contains("POSTPONED", page);
    }

    [Fact]
    public void Every_locale_renders_the_same_pages()
    {
        var digest = BuildDigest("ucl-pre-20261104.json", "uefa.champions", DigestKind.Upcoming);

        Assert.Equal(2, CardLocale.All.Count);
        Assert.All(CardLocale.All, locale => Assert.NotEmpty(Html.BuildPages(digest, locale)));
    }

    [Fact]
    public void Pages_are_split_without_page_numbers()
    {
        var kickoffs = Enumerable.Repeat(Night, CardPaging.MaxPerPage + 1).ToArray();

        var pages = Html.BuildPages(Upcoming(kickoffs), CardLocale.Vietnamese);

        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.Contains("<footer><span>Trang &lt;test&gt;</span></footer>", page));
    }

    private static Digest Results(params DateTimeOffset[] kickoffs) => new(
        DigestKind.Results,
        kickoffs.Select((k, i) => TestData.Match(i.ToString(), k, MatchStatus.Finished, homeScore: 1, awayScore: 0)).ToList(),
        []);

    [Fact]
    public void Competition_name_sits_under_the_title_without_a_subtitle()
    {
        var page = SinglePage(Results(DayBeforeEvening, Night), CardLocale.Vietnamese);

        Assert.Contains($"<h1>{WebUtility.HtmlEncode("KẾT QUẢ")}</h1><p class=\"competition\">{WebUtility.HtmlEncode("NGOẠI HẠNG ANH")}</p></div>", page);
        Assert.DoesNotContain("<div class=\"date\">", page);
        Assert.DoesNotContain(WebUtility.HtmlEncode("Đêm qua"), page);
        Assert.DoesNotContain("<h2>", page);
    }

    [Fact]
    public void Section_heading_shows_the_round()
    {
        var digest = Results(Night, Night);
        digest = digest with { Matches = digest.Matches.Select(m => m with { Round = 11 }).ToList() };

        var vi = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains($"<h2>{WebUtility.HtmlEncode("LƯỢT TRẬN 11/38")}</h2>", vi);
        Assert.Equal(1, vi.Split(WebUtility.HtmlEncode("NGOẠI HẠNG ANH")).Length - 1);

        Assert.Contains("<h2>MATCHDAY 11 OF 38</h2>", SinglePage(digest, CardLocale.English));

        var unknownTotal = digest with { Matches = digest.Matches.Select(m => m with { Competition = m.Competition with { Rounds = null } }).ToList() };
        Assert.Contains($"<h2>{WebUtility.HtmlEncode("LƯỢT TRẬN 11")}</h2>", SinglePage(unknownTotal, CardLocale.Vietnamese));
    }

    [Fact]
    public void Each_round_has_its_own_section_earlier_round_first()
    {
        var digest = Results(Night, Night, Night);
        digest = digest with { Matches = [digest.Matches[0] with { Round = 10 }, digest.Matches[1] with { Round = 9 }, digest.Matches[2] with { Round = 10 }] };

        var page = SinglePage(digest, CardLocale.Vietnamese);

        var nine = page.IndexOf($"<h2>{WebUtility.HtmlEncode("LƯỢT TRẬN 9/38")}</h2>");
        var ten = page.IndexOf($"<h2>{WebUtility.HtmlEncode("LƯỢT TRẬN 10/38")}</h2>");
        Assert.True(nine >= 0 && ten > nine);
        Assert.Equal(1, page[nine..ten].Split("<article>").Length - 1);
        Assert.Equal(2, page[ten..].Split("<article>").Length - 1);
    }

    [Fact]
    public void Form_note_appears_once_per_competition()
    {
        var digest = Upcoming(Night, Night);
        digest = digest with { Matches = digest.Matches.Select((m, i) => m with { Round = 11 + i, Home = m.Home with { Form = "W" } }).ToList() };

        Assert.Equal(1, SinglePage(digest, CardLocale.Vietnamese).Split("class=\"note\"").Length - 1);
    }

    [Fact]
    public void Knockout_section_shows_stage_and_leg()
    {
        var digest = Upcoming(Night, Night);
        digest = digest with { Matches = digest.Matches.Select(m => m with { Stage = "round-of-16", Leg = 1 }).ToList() };

        Assert.Contains($"<h2>{WebUtility.HtmlEncode("VÒNG 1/8 · LƯỢT ĐI")}</h2>", SinglePage(digest, CardLocale.Vietnamese));
        Assert.Contains($"<h2>{WebUtility.HtmlEncode("ROUND OF 16 · 1ST LEG")}</h2>", SinglePage(digest, CardLocale.English));
    }

    [Fact]
    public void Merged_page_lists_every_competition_and_names_each_section()
    {
        var digest = new Digest(DigestKind.Results, [
            TestData.Match("1", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0) with { Round = 11 },
            TestData.Match("2", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0) with { Round = 11 },
            TestData.Match("3", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0) with { Competition = TestData.Competition("ita.1"), Round = 10 },
        ], []);

        var page = SinglePage(digest, CardLocale.Vietnamese);

        Assert.Contains($"<p class=\"competition\">{WebUtility.HtmlEncode("NGOẠI HẠNG ANH · SERIE A")}</p>", page);
        Assert.Contains($"<h2>{WebUtility.HtmlEncode("NGOẠI HẠNG ANH · LƯỢT TRẬN 11/38")}</h2>", page);
        Assert.Contains($"<h2>{WebUtility.HtmlEncode("SERIE A · LƯỢT TRẬN 10/38")}</h2>", page);
    }

    [Fact]
    public void Results_show_date_and_time_under_every_match()
    {
        var digest = Results(Night, Night);

        Assert.Contains("<div class=\"pen\">05/11 · 03:00</div>", SinglePage(digest, CardLocale.Vietnamese));
        Assert.Contains("<div class=\"pen\">4 Nov · 20:00</div>", SinglePage(digest, CardLocale.English));
    }

    [Fact]
    public void Results_over_many_days_have_no_date_range()
    {
        var page = SinglePage(Results(Night.AddDays(-6), Night.AddDays(-3), Night), CardLocale.Vietnamese);

        Assert.DoesNotContain("<div class=\"date\">", page);
        Assert.DoesNotContain("30/10 –", page);
    }

    [Fact]
    public void Each_competition_has_its_own_page()
    {
        var digest = new Digest(DigestKind.Upcoming, [
            TestData.Match("1", Night) with { Competition = TestData.Competition("uefa.europa") },
            TestData.Match("2", Night) with { Competition = TestData.Competition("uefa.europa") },
            TestData.Match("3", Night),
            TestData.Match("4", Night),
        ], []);

        var pages = Html.BuildPages(digest, CardLocale.Vietnamese);

        Assert.Equal(2, pages.Count);
        Assert.Contains("EUROPA LEAGUE", pages[0]);
        Assert.DoesNotContain("NGOẠI HẠNG ANH", pages[0]);
        Assert.Contains("NGOẠI HẠNG ANH", pages[1]);
    }

    [Fact]
    public void Single_match_digest_still_has_an_image()
    {
        Assert.Contains(">03:00<", SinglePage(Upcoming(Night), CardLocale.Vietnamese));
    }

    [Fact]
    public void Page_logo_is_shown_only_when_provided()
    {
        var digest = Upcoming(Night, Night);

        Assert.DoesNotContain("page-logo\" src", SinglePage(digest, CardLocale.Vietnamese));
        Assert.Contains("data:image/png;base64,QUJD", new CardHtml(TestData.Clubs, Theme, "", "", "QUJD").BuildPages(digest, CardLocale.Vietnamese).Single());
    }

    [Fact]
    public void Upcoming_page_shows_the_encoded_venue_in_both_languages()
    {
        var match = TestData.Match("1", Night) with { Venue = "St <James'> Park" };
        var digest = new Digest(DigestKind.Upcoming, [match], []);

        Assert.All(CardLocale.All, locale =>
            Assert.Contains($"<div class=\"venue\">{WebUtility.HtmlEncode(match.Venue)}</div>", SinglePage(digest, locale)));
    }

    [Fact]
    public void Upcoming_page_without_venue_has_no_venue_line()
    {
        Assert.DoesNotContain("<div class=\"venue\">", SinglePage(Upcoming(Night), CardLocale.Vietnamese));
    }

    [Fact]
    public void Results_and_updates_do_not_show_the_venue()
    {
        var finished = TestData.Match("1", Night, MatchStatus.Finished, homeScore: 1, awayScore: 0) with { Venue = "Anfield" };
        var moved = TestData.Match("2", Night) with { Venue = "Anfield" };

        Assert.DoesNotContain("Anfield", SinglePage(new Digest(DigestKind.Results, [finished], []), CardLocale.Vietnamese));
        Assert.DoesNotContain("Anfield", SinglePage(new Digest(DigestKind.Upcoming, [], [moved]), CardLocale.Vietnamese));
    }

    [Fact]
    public void Match_rows_use_the_theme_gradient()
    {
        Assert.Contains("linear-gradient(90deg, #111, #222)", SinglePage(Upcoming(Night, Night), CardLocale.Vietnamese));
    }

    [Fact]
    public void Real_theme_config_can_be_loaded()
    {
        var theme = CardTheme.Load(Path.Combine(FindConfigDirectory(), "card.json"));

        Assert.All(new[] { theme.Surface, theme.SurfaceEnd, theme.SurfaceText, theme.SurfaceMuted }, Assert.NotEmpty);
        Assert.All(TestData.Competitions, c => Assert.True(theme.Competitions?.ContainsKey(c.Slug), $"Thiếu màu riêng của {c.Slug}"));
        Assert.Equal(theme.PageName, theme.For("eng.1").PageName);
    }

    [Fact]
    public void Each_competition_uses_its_own_colours_and_falls_back_to_the_default()
    {
        var theme = Theme with { Competitions = new Dictionary<string, ThemeColors> { ["ita.1"] = new(Background: "#0E1B3D", Accent: "#7CC8FF") } };
        var html = new CardHtml(TestData.Clubs, theme, "", "");
        Match Serie(string id) => TestData.Match(id, Night) with { Competition = TestData.Competition("ita.1") };

        var serie = html.BuildPages(new Digest(DigestKind.Upcoming, [Serie("1"), Serie("2")], []), CardLocale.Vietnamese).Single();
        Assert.Contains("background: #0E1B3D", serie);
        Assert.Contains("color: #7CC8FF", serie);
        Assert.Contains("linear-gradient(90deg, #111, #222)", serie);

        var english = html.BuildPages(Upcoming(Night, Night), CardLocale.Vietnamese).Single();
        Assert.DoesNotContain("#0E1B3D", english);
        Assert.Contains("background: #000", english);
    }

    private static string FindConfigDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var config = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(config, "card.json")))
                return config;
        }
        throw new DirectoryNotFoundException("Không tìm thấy config/card.json");
    }

    [Fact]
    public void Text_is_html_encoded()
    {
        var digest = new Digest(DigestKind.Upcoming, [TestData.Match("1", Night, homeId: "1", homeName: "<script>"), TestData.Match("2", Night)], []);

        var page = SinglePage(digest, CardLocale.Vietnamese);

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("Trang &lt;test&gt;", page);
    }
}

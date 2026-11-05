using FootballNews.Core.Filtering;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class CardHtmlTests
{
    private static readonly CardTheme Theme = new("Trang <test>", "#000", "#111", "#fff", "#eee", "#ccc");
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
    public void Upcoming_page_shows_vietnam_time()
    {
        var pages = Html.BuildPages(BuildDigest("ucl-pre-20261104.json", "uefa.champions", DigestKind.Upcoming), CardLocale.Vietnamese);

        Assert.Contains("00:45", string.Concat(pages));
        Assert.Contains("03:00", string.Concat(pages));
    }

    [Fact]
    public void Single_day_shows_weekday_and_full_date_in_both_languages()
    {
        Assert.Contains("Thứ Năm, 05/11/2026", SinglePage(Upcoming(Night, SameDayEvening), CardLocale.Vietnamese));
        Assert.Contains("Thursday, 5 Nov 2026", SinglePage(Upcoming(SameDayEvening, SameDayEvening), CardLocale.English));
    }

    [Fact]
    public void Each_language_uses_its_own_time_zone_for_time_and_date()
    {
        var digest = Upcoming(Night, Night);

        var vietnamese = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains(">03:00<", vietnamese);
        Assert.Contains("Thứ Năm, 05/11/2026", vietnamese);

        var english = SinglePage(digest, CardLocale.English);
        Assert.Contains(">20:00<", english);
        Assert.Contains("Wednesday, 4 Nov 2026", english);
    }

    [Fact]
    public void English_time_follows_british_summer_time()
    {
        var summer = new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero);
        var page = SinglePage(Upcoming(summer, summer), CardLocale.English);

        Assert.Contains(">19:00<", page);
    }

    [Fact]
    public void Single_day_has_no_per_match_date()
    {
        Assert.DoesNotContain("<div class=\"pen\">", SinglePage(Upcoming(Night, SameDayEvening), CardLocale.Vietnamese));
    }

    [Fact]
    public void Several_days_show_range_and_per_match_date()
    {
        var digest = Upcoming(DayBeforeEvening, SameDayEvening);

        var vietnamese = SinglePage(digest, CardLocale.Vietnamese);
        Assert.Contains("04/11 – 05/11/2026", vietnamese);
        Assert.Contains("<div class=\"pen\">04/11</div>", vietnamese);
        Assert.Contains("<div class=\"pen\">05/11</div>", vietnamese);

        var english = SinglePage(digest, CardLocale.English);
        Assert.Contains("4 Nov – 5 Nov 2026", english);
        Assert.Contains("<div class=\"pen\">5 Nov</div>", english);
    }

    [Fact]
    public void Date_uses_vietnam_time_not_utc()
    {
        // 20:00 ngày 04/11 theo UTC đã là ngày 05/11 ở Việt Nam.
        Assert.DoesNotContain("04/11/2026", SinglePage(Upcoming(Night, Night), CardLocale.Vietnamese));
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
    public void Pages_are_split_and_numbered()
    {
        var kickoffs = Enumerable.Repeat(Night, CardPaging.MaxPerPage + 1).ToArray();

        var pages = Html.BuildPages(Upcoming(kickoffs), CardLocale.Vietnamese);

        Assert.Equal(2, pages.Count);
        Assert.Contains("1/2", pages[0]);
        Assert.Contains("2/2", pages[1]);
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
    public void Text_is_html_encoded()
    {
        var digest = new Digest(DigestKind.Upcoming, [TestData.Match("1", Night, homeId: "1", homeName: "<script>"), TestData.Match("2", Night)], []);

        var page = SinglePage(digest, CardLocale.Vietnamese);

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("Trang &lt;test&gt;", page);
    }
}

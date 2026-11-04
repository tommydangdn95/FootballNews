using FootballNews.Core.Filtering;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class CardHtmlTests
{
    private static readonly CardTheme Theme = new("Trang <test>", "#000", "#111", "#fff", "#eee", "#ccc");
    private static readonly CardHtml Html = new(TestData.Clubs, Theme, "", "");

    private static Digest BuildDigest(string fixture, string slug, DigestKind kind)
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture(fixture), TestData.Competition(slug));
        return new Digest(kind, BigClubFilter.Apply(matches, TestData.Clubs).ToList(), []);
    }

    [Fact]
    public void Results_page_has_logo_score_and_scorer()
    {
        var page = Html.BuildPages(BuildDigest("eng1-20261101.json", "eng.1", DigestKind.Results))
            .Single(p => p.Contains("Havertz"));

        Assert.Contains("364.png", page);
        Assert.Contains("0–1", page);
        Assert.Contains("NGOẠI HẠNG ANH", page);
    }

    [Fact]
    public void Upcoming_page_shows_vietnam_time()
    {
        var pages = Html.BuildPages(BuildDigest("ucl-pre-20261104.json", "uefa.champions", DigestKind.Upcoming));

        Assert.Contains("00:45", string.Concat(pages));
        Assert.Contains("03:00", string.Concat(pages));
    }

    [Fact]
    public void Pages_are_split_and_numbered()
    {
        var matches = Enumerable.Range(0, CardHtml.MaxMatchesPerPage + 1)
            .Select(i => TestData.Match(i.ToString(), DateTimeOffset.UnixEpoch))
            .ToList();

        var pages = Html.BuildPages(new Digest(DigestKind.Upcoming, matches, []));

        Assert.Equal(2, pages.Count);
        Assert.Contains("1/2", pages[0]);
        Assert.Contains("2/2", pages[1]);
    }

    [Fact]
    public void Page_logo_is_shown_only_when_provided()
    {
        var digest = new Digest(DigestKind.Upcoming, [TestData.Match("1", DateTimeOffset.UnixEpoch)], []);

        Assert.DoesNotContain("page-logo\" src", Html.BuildPages(digest).Single());
        Assert.Contains("data:image/png;base64,QUJD", new CardHtml(TestData.Clubs, Theme, "", "", "QUJD").BuildPages(digest).Single());
    }

    [Fact]
    public void Text_is_html_encoded()
    {
        var page = Html.BuildPages(new Digest(DigestKind.Upcoming, [TestData.Match("1", DateTimeOffset.UnixEpoch, homeId: "1", homeName: "<script>")], [])).Single();

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("Trang &lt;test&gt;", page);
    }
}

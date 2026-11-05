using FootballNews.Core.Filtering;
using FootballNews.Core.Formatting;
using FootballNews.Core.Models;
using FootballNews.Sources.Espn;

namespace FootballNews.Tests;

public class FormatterTests
{
    private static Digest LiverpoolArsenal()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("eng1-20261101.json"), TestData.Competition("eng.1"));
        return new Digest(DigestKind.Results, [matches.Single(m => m.Id == "401879246")], []);
    }

    private static Digest UclPost()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("ucl-post-20261103.json"), TestData.Competition("uefa.champions"));
        return new Digest(DigestKind.Results, matches, []);
    }

    private static Digest UclPre()
    {
        var matches = EspnParser.ParseScoreboard(TestData.Fixture("ucl-pre-20261104.json"), TestData.Competition("uefa.champions"));
        return new Digest(DigestKind.Upcoming, matches, []);
    }

    [Fact]
    public void Facebook_results_match_spec_example()
    {
        var text = new FacebookFormatter(TestData.Clubs).Format(LiverpoolArsenal()).Parts.Single();

        Assert.Equal(
            """
            ⚽ KẾT QUẢ ĐÊM QUA & RẠNG SÁNG NAY

            🏴 NGOẠI HẠNG ANH
            Liverpool 0–1 Arsenal
            ⚽ Havertz 16'
            🟨 Liverpool 4 – Arsenal 4

            #NgoaiHangAnh #Liverpool #Arsenal
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void Facebook_upcoming_shows_vietnam_time_and_form()
    {
        var text = new FacebookFormatter(TestData.Clubs).Format(UclPre()).Parts.Single();

        Assert.Contains("⭐ CHAMPIONS LEAGUE", text);
        Assert.Contains("🕒 00:45 05/11 | AEK Athens vs Real Madrid", text);
        Assert.Contains("🕒 03:00 05/11 | Leipzig vs Man City", text);
        Assert.Contains("Phong độ: DWLLW | WWWWW", text);
    }

    [Fact]
    public void Facebook_includes_matches_without_a_big_club()
    {
        var text = new FacebookFormatter(TestData.Clubs).Format(UclPre()).Parts.Single();

        Assert.Contains("Viking FK", text);
    }

    [Fact]
    public void Facebook_shows_updates_first()
    {
        var digest = new Digest(
            DigestKind.Upcoming,
            [],
            [TestData.Match("1", new DateTimeOffset(2026, 11, 6, 20, 0, 0, TimeSpan.Zero)), TestData.Match("2", DateTimeOffset.UnixEpoch, MatchStatus.Postponed)]);

        var text = new FacebookFormatter(TestData.Clubs).Format(digest).Parts.Single();

        Assert.Contains("⚠️ CẬP NHẬT", text);
        Assert.Contains("Giờ mới 03:00 07/11 | Liverpool vs Arsenal", text);
        Assert.Contains("⛔ HOÃN: Liverpool vs Arsenal", text);
    }

    [Fact]
    public void Threads_splits_into_posts_within_limit_and_keeps_every_match()
    {
        var many = Enumerable.Range(0, 40).Select(i => TestData.Match(i.ToString(), DateTimeOffset.UnixEpoch, MatchStatus.Finished, homeScore: 1, awayScore: 0)).ToList();

        var parts = new ThreadsFormatter(TestData.Clubs).Format(new Digest(DigestKind.Results, many, [])).Parts;

        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(p.Length <= ThreadsFormatter.MaxLength, $"Dài {p.Length} ký tự"));
        Assert.Equal(many.Count, parts.Sum(p => p.Split('\n').Count(line => line.Contains('–'))));
        Assert.Contains("#NgoaiHangAnh", parts[^1]);
    }

    [Fact]
    public void Threads_real_data_stays_within_limit_and_starts_with_top_matches()
    {
        var parts = new ThreadsFormatter(TestData.Clubs).Format(UclPost()).Parts;

        Assert.All(parts, p => Assert.True(p.Length <= ThreadsFormatter.MaxLength, $"Dài {p.Length} ký tự"));
        Assert.Contains("Atlético Madrid 0–2 Bayern", parts[0]);
    }

    [Fact]
    public void X_tweets_stay_within_weighted_limit()
    {
        var updates = Enumerable.Range(0, 12).Select(i => TestData.Match(i.ToString(), DateTimeOffset.UnixEpoch, MatchStatus.Postponed)).ToList();

        var draft = new XFormatter(TestData.Clubs).Format(new Digest(DigestKind.Upcoming, UclPre().Matches, updates));

        Assert.True(draft.Parts.Count > 1);
        Assert.All(draft.Parts, tweet => Assert.True(XFormatter.WeightedLength(tweet) <= XFormatter.MaxWeightedLength));
    }

    [Fact]
    public void X_shows_every_match()
    {
        var digest = UclPost();

        var draft = new XFormatter(TestData.Clubs).Format(digest);

        Assert.Equal(digest.Matches.Count, draft.Parts.Sum(p => p.Split('\n').Count(line => line.Contains('–'))));
    }

    [Fact]
    public void Upcoming_on_x_and_threads_is_chronological()
    {
        var late = TestData.Match("1", new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero));
        var early = TestData.Match("2", new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero));
        var digest = new Digest(DigestKind.Upcoming, [late, early], []);

        foreach (var text in new[] { new XFormatter(TestData.Clubs).Format(digest).Parts[0], new ThreadsFormatter(TestData.Clubs).Format(digest).Parts[0] })
            Assert.True(text.IndexOf("06/11", StringComparison.Ordinal) < text.IndexOf("09/11", StringComparison.Ordinal));
    }

    [Fact]
    public void X_hashtags_stay_with_the_last_matches()
    {
        var many = Enumerable.Range(0, 40).Select(i => TestData.Match(i.ToString(), DateTimeOffset.UnixEpoch, MatchStatus.Finished, homeScore: 1, awayScore: 0)).ToList();

        var draft = new XFormatter(TestData.Clubs).Format(new Digest(DigestKind.Results, many, []));

        Assert.True(draft.Parts.Count > 1);
        Assert.All(draft.Parts, tweet => Assert.True(XFormatter.WeightedLength(tweet) <= XFormatter.MaxWeightedLength));
        Assert.Contains("–", draft.Parts[^1]);
        Assert.Contains("#NgoaiHangAnh", draft.Parts[^1]);
    }

    [Theory]
    [InlineData("abc", 3)]
    [InlineData("–", 1)]
    [InlineData("đ", 1)]
    [InlineData("ả", 2)]
    [InlineData("⚽", 2)]
    public void X_weight_follows_x_rules(string text, int expected)
    {
        Assert.Equal(expected, XFormatter.WeightedLength(text));
    }
}

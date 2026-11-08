using System.Text.Json;
using FootballNews.Core.Formatting;
using FootballNews.Core.Live;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;
using FootballNews.Telegram;

namespace FootballNews.Tests;

public class LiveTrackerTests
{
    private static readonly DateTimeOffset Kickoff = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T0 = Kickoff.AddMinutes(30);

    internal static Match Live(int home, int away, string status = "STATUS_FIRST_HALF", int period = 1, MatchStatus state = MatchStatus.Live, params MatchEvent[] events) =>
        TestData.Match("1", Kickoff, state, homeId: "359", homeName: "Arsenal", awayId: "306", awayName: "Hull City", homeScore: home, awayScore: away)
            with { Events = events, StatusName = status, Period = period };

    internal static MatchEvent Goal(string team, string player, string minute, MatchEventType type = MatchEventType.Goal) => new(type, minute, player, team);

    private static (LiveState State, LiveSession Session) Start(Match match)
    {
        var state = new LiveState { Watch = LiveTracker.Start(match) };
        return (state, new LiveSession(state, TestData.Clubs, TestData.Competition("eng.1"), "MatchdayChallenge"));
    }

    [Fact]
    public void Starting_mid_match_does_not_report_what_already_happened()
    {
        var (state, session) = Start(Live(1, 0, events: Goal("359", "Saka", "12'")));

        var update = session.Apply(Live(1, 0, events: Goal("359", "Saka", "12'")), T0);

        Assert.Empty(update.NewDrafts);
        Assert.Equal(1, state.Watch!.HomeScore);
    }

    [Fact]
    public void New_goal_becomes_a_draft_with_the_score_at_that_moment()
    {
        var (_, session) = Start(Live(0, 0));

        var update = session.Apply(Live(1, 0, events: Goal("359", "Rice", "23'")), T0);

        var draft = Assert.Single(update.NewDrafts);
        Assert.Equal(LiveEventKind.Goal, draft.Kind);
        Assert.Equal((1, 0), (draft.HomeScore, draft.AwayScore));
        Assert.Equal("BÀN THẮNG!", draft.Title);
        Assert.Equal("Rice 23'", draft.Detail);
        Assert.Contains("mở tỉ số cho Arsenal", draft.Text);
        Assert.Contains("#Arsenal #HullCity #NgoaiHangAnh #MatchdayChallenge", draft.Caption);
        Assert.Equal(DraftStatus.Queued, draft.Status);
    }

    [Fact]
    public void Two_goals_between_polls_become_two_drafts_in_order()
    {
        var (_, session) = Start(Live(0, 0));

        var update = session.Apply(Live(1, 1, events: [Goal("306", "Smith", "31'"), Goal("359", "Rice", "23'")]), T0);

        Assert.Equal(2, update.NewDrafts.Count);
        Assert.Equal((1, 0), (update.NewDrafts[0].HomeScore, update.NewDrafts[0].AwayScore));
        Assert.Equal((1, 1), (update.NewDrafts[1].HomeScore, update.NewDrafts[1].AwayScore));
        Assert.True(update.NewDrafts[0].Id < update.NewDrafts[1].Id);
    }

    [Fact]
    public void Goal_without_details_yet_still_reports_the_team()
    {
        var (_, session) = Start(Live(0, 0));

        var draft = Assert.Single(session.Apply(Live(0, 1), T0).NewDrafts);

        Assert.Null(draft.Detail);
        Assert.Contains("Hull City ghi bàn", draft.Text);
    }

    [Fact]
    public void Cancelled_goal_that_was_posted_gets_a_var_draft()
    {
        var (state, session) = Start(Live(0, 0));
        var goal = Assert.Single(session.Apply(Live(1, 0, events: Goal("359", "Rice", "23'")), T0).NewDrafts);
        goal.Status = DraftStatus.Posted;

        var update = session.Apply(Live(0, 0), T0.AddMinutes(4));

        var var = Assert.Single(update.NewDrafts);
        Assert.Equal(LiveEventKind.GoalCancelled, var.Kind);
        Assert.Equal((0, 0), (var.HomeScore, var.AwayScore));
        Assert.Equal("Bàn của Rice 23'", var.Detail);
        Assert.Equal(0, state.Watch!.HomeScore);
    }

    [Fact]
    public void Cancelled_goal_still_pending_only_gets_a_warning()
    {
        var (_, session) = Start(Live(0, 0));
        var goal = Assert.Single(session.Apply(Live(1, 0, events: Goal("359", "Rice", "23'")), T0).NewDrafts);

        var update = session.Apply(Live(0, 0), T0.AddMinutes(4));

        Assert.Empty(update.NewDrafts);
        Assert.Same(goal, Assert.Single(update.VarWarnings));
        Assert.True(goal.VarWarning);
    }

    [Theory]
    [InlineData(DraftStatus.Skipped)]
    [InlineData(DraftStatus.Expired)]
    public void Cancelled_goal_that_was_not_posted_creates_nothing(DraftStatus status)
    {
        var (_, session) = Start(Live(0, 0));
        Assert.Single(session.Apply(Live(1, 0, events: Goal("359", "Rice", "23'")), T0).NewDrafts).Status = status;

        var update = session.Apply(Live(0, 0), T0.AddMinutes(4));

        Assert.Empty(update.NewDrafts);
        Assert.Empty(update.VarWarnings);
    }

    [Fact]
    public void Espn_correcting_the_scorer_is_not_a_new_goal()
    {
        var (_, session) = Start(Live(0, 0));
        session.Apply(Live(1, 0, events: Goal("359", "Rice", "23'")), T0);

        var update = session.Apply(Live(1, 0, events: Goal("359", "Saka", "24'")), T0.AddMinutes(4));

        Assert.Empty(update.NewDrafts);
    }

    [Fact]
    public void Red_card_is_reported_once()
    {
        var (_, session) = Start(Live(0, 0));
        var card = new MatchEvent(MatchEventType.RedCard, "61'", "Smith", "306");

        var first = session.Apply(Live(0, 0, events: card), T0);
        var second = session.Apply(Live(0, 0, events: card with { Minute = "62'" }), T0.AddMinutes(4));

        var draft = Assert.Single(first.NewDrafts);
        Assert.Equal(LiveEventKind.RedCard, draft.Kind);
        Assert.Equal("Smith 61' (Hull City)", draft.Detail);
        Assert.Empty(second.NewDrafts);
    }

    [Fact]
    public void Half_time_is_reported_once_and_skipped_if_missed()
    {
        var (_, session) = Start(Live(0, 0));
        var ht = session.Apply(Live(1, 0, "STATUS_HALFTIME", 1, events: Goal("359", "Rice", "23'")), T0);
        Assert.Equal([LiveEventKind.Goal, LiveEventKind.HalfTime], ht.NewDrafts.Select(d => d.Kind));
        Assert.Empty(session.Apply(Live(1, 0, "STATUS_HALFTIME", 1, events: Goal("359", "Rice", "23'")), T0.AddMinutes(4)).NewDrafts);

        var (_, late) = Start(Live(0, 0));
        Assert.Empty(late.Apply(Live(0, 0, "STATUS_SECOND_HALF", 2), T0).NewDrafts);
        Assert.Empty(late.Apply(Live(0, 0, "STATUS_HALFTIME", 1), T0).NewDrafts);
    }

    [Fact]
    public void Full_time_without_extra_time()
    {
        var (_, session) = Start(Live(2, 1, "STATUS_SECOND_HALF", 2, events: [Goal("359", "A", "10'"), Goal("359", "B", "20'"), Goal("306", "C", "30'")]));

        var draft = Assert.Single(session.Apply(Live(2, 1, "STATUS_FULL_TIME", 2, MatchStatus.Finished), T0).NewDrafts);

        Assert.Equal(LiveEventKind.FullTime, draft.Kind);
        Assert.Equal("KẾT THÚC", draft.Title);
        Assert.Contains("Kết thúc: Arsenal 2-1 Hull City", draft.Text);
        // ESPN trả danh sách sự kiện rỗng lúc hết trận: vẫn có tên người ghi bàn từ những bàn bot đã thấy.
        Assert.Equal(["A 10'", "B 20'"], draft.HomeLines!);
        Assert.Equal(["C 30'"], draft.AwayLines!);
    }

    [Fact]
    public void Half_time_and_full_time_list_scorers_and_red_cards()
    {
        var (_, session) = Start(Live(0, 0));
        MatchEvent[] firstHalf =
        [
            Goal("359", "Saka", "12'"),
            Goal("306", "Smith", "30'", MatchEventType.PenaltyGoal),
            new(MatchEventType.RedCard, "40'", "Jones", "306"),
            Goal("359", "Rice", "45'+2'"),
        ];
        var ht = session.Apply(Live(2, 1, "STATUS_HALFTIME", 1, events: firstHalf), T0).NewDrafts.Last();

        Assert.Equal(LiveEventKind.HalfTime, ht.Kind);
        Assert.Null(ht.Detail);
        Assert.Equal(["Saka 12'", "Rice 45'+2'"], ht.HomeLines!);
        Assert.Equal(["Smith 30' (pen)", $"Jones 40' {Draft.RedCardMark}"], ht.AwayLines!);
        Assert.Equal(
            "⏸️ Hết hiệp 1: Arsenal 2-1 Hull City\n⚽ Arsenal: Saka 12', Rice 45'+2'\n⚽ Hull City: Smith 30' (pen)\n🟥 Thẻ đỏ: Jones 40' (Hull City)",
            ht.Text);

        var ft = session.Apply(Live(3, 1, "STATUS_FULL_TIME", 2, MatchStatus.Finished, [.. firstHalf, Goal("306", "Gabriel", "80'", MatchEventType.OwnGoal)]), T0.AddHours(1)).NewDrafts.Last();
        Assert.Equal(LiveEventKind.FullTime, ft.Kind);
        Assert.Contains("⚽ Arsenal: Saka 12', Rice 45'+2'", ft.Text);
        Assert.Contains("Hull City: Smith 30' (pen), Gabriel 80' (phản lưới)", ft.Text);
    }

    [Fact]
    public void Goalless_half_time_has_no_summary()
    {
        var (_, session) = Start(Live(0, 0));
        var ht = Assert.Single(session.Apply(Live(0, 0, "STATUS_HALFTIME", 1), T0).NewDrafts);
        Assert.Null(ht.Detail);
        Assert.Null(ht.HomeLines);
        Assert.Null(ht.AwayLines);
        Assert.Equal("⏸️ Hết hiệp 1: Arsenal 0-0 Hull City", ht.Text);
    }

    [Fact]
    public void Extra_time_reports_end_of_ninety_then_end_of_extra_time_and_nothing_about_penalties()
    {
        var (_, session) = Start(Live(1, 1, "STATUS_SECOND_HALF", 2, events: [Goal("359", "A", "10'"), Goal("306", "C", "30'")]));

        // Hỏi ESPN lần đầu khi đã vào hiệp phụ và có bàn ở phút 95: tỉ số sau 90 phút vẫn là 1-1.
        var et = session.Apply(Live(2, 1, "STATUS_FIRST_HALF_EXTRA_TIME", 3, events: [Goal("359", "A", "10'"), Goal("306", "C", "30'"), Goal("359", "D", "95'")]), T0);
        Assert.Equal([LiveEventKind.Goal, LiveEventKind.EndOfRegulation], et.NewDrafts.Select(d => d.Kind));
        Assert.Equal((1, 1), (et.NewDrafts[1].HomeScore, et.NewDrafts[1].AwayScore));

        var pens = session.Apply(Live(2, 2, "STATUS_SHOOTOUT", 5, events: [Goal("359", "A", "10'"), Goal("306", "C", "30'"), Goal("359", "D", "95'"), Goal("306", "E", "118'")]), T0.AddMinutes(30));
        Assert.Equal([LiveEventKind.Goal, LiveEventKind.FullTime], pens.NewDrafts.Select(d => d.Kind));
        Assert.Equal("HẾT HIỆP PHỤ", pens.NewDrafts[1].Title);

        Assert.Empty(session.Apply(Live(2, 2, "STATUS_FINAL_PEN", 5, MatchStatus.Finished), T0.AddMinutes(45)).NewDrafts);
    }

    [Fact]
    public void Suspended_match_is_not_mistaken_for_penalties()
    {
        var (_, session) = Start(Live(0, 0));
        Assert.Empty(session.Apply(Live(0, 0, "STATUS_SUSPENDED", 2, MatchStatus.Postponed), T0).NewDrafts);
    }

    [Theory]
    [InlineData("45'", "45'+2'")]
    [InlineData("45'+2'", "46'")]
    [InlineData("90'+7'", "91'")]
    public void Minute_order(string earlier, string later) =>
        Assert.True(LiveTracker.MinuteOrder(earlier) < LiveTracker.MinuteOrder(later));
}

public class LiveQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 15, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    [Fact]
    public void Only_one_draft_is_active_and_the_rest_wait_in_order()
    {
        var state = new LiveState();
        var first = state.Enqueue(new Draft(), T0);
        var second = state.Enqueue(new Draft(), T0.AddMinutes(1));

        Assert.Same(first, state.PromoteNext());
        Assert.Null(state.PromoteNext());
        Assert.Equal(DraftStatus.Queued, second.Status);

        first.Status = DraftStatus.Skipped;
        Assert.Same(second, state.PromoteNext());
    }

    [Fact]
    public void Drafts_expire_ten_minutes_after_detection_even_while_queued()
    {
        var state = new LiveState();
        var first = state.Enqueue(new Draft(), T0);
        var second = state.Enqueue(new Draft(), T0.AddMinutes(4));
        state.PromoteNext();
        state.EditingDraftId = first.Id;

        Assert.Empty(state.ExpireDue(T0.AddMinutes(9), Lifetime));
        Assert.Equal([first], state.ExpireDue(T0.AddMinutes(10), Lifetime));
        Assert.Null(state.EditingDraftId);
        Assert.Equal([second], state.ExpireDue(T0.AddMinutes(14), Lifetime));
        Assert.False(state.HasPending);
    }

    [Fact]
    public void Edit_replaces_image_lines_and_caption_text_but_keeps_hashtags()
    {
        var draft = new Draft { Text = "⚽ Rice ghi bàn", Hashtags = "#Arsenal" };

        LiveSession.ApplyEdit(draft, "  VÀOOO!\r\n\nRice sút xa  \n");

        Assert.Equal(["VÀOOO!", "Rice sút xa"], draft.CustomLines);
        Assert.Equal("VÀOOO!\nRice sút xa\n\n#Arsenal", draft.Caption);
    }

    [Fact]
    public void State_round_trips_through_json_without_computed_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"live-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LiveStore(path);
            var state = new LiveState { Watch = LiveTracker.Start(LiveTrackerTests.Live(1, 0, events: LiveTrackerTests.Goal("359", "Rice", "23'"))) };
            state.Enqueue(new Draft { Kind = LiveEventKind.Goal, Text = "x", Hashtags = "#a", GoalId = "g1" }, T0);
            store.Save(state);

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("\"caption\"", json);
            Assert.DoesNotContain("\"active\"", json);

            var loaded = store.Load();
            Assert.Equal("Rice", loaded.Watch!.Goals.Single().Player);
            Assert.Equal(LiveEventKind.Goal, loaded.Drafts.Single().Kind);
            Assert.Equal(2, loaded.NextDraftId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class LiveSupportTests
{
    [Theory]
    [InlineData("arsenal hull", true)]
    [InlineData("ARSENAL", true)]
    [InlineData("atletico", true)]
    [InlineData("arsenal chelsea", false)]
    [InlineData("", false)]
    public void Search_matches_every_word_ignoring_case_and_accents(string query, bool expected) =>
        Assert.Equal(expected, TextSearch.MatchesAllWords(query, "Arsenal Hull City Atlético Madrid"));

    [Fact]
    public void Hashtag_from_name_drops_spaces_and_accents() =>
        Assert.Equal("AtleticoMadrid", Hashtags.FromName("Atlético Madrid"));

    [Fact]
    public void Parses_status_name_and_period_from_live_summary()
    {
        var match = EspnParser.ParseSummary(TestData.Fixture("summary-401878760-ht.json"), "401878760", TestData.Competition("eng.1"));

        Assert.Equal(MatchStatus.Live, match.Status);
        Assert.Equal("STATUS_HALFTIME", match.StatusName);
        Assert.Equal(1, match.Period);
        Assert.Equal("Arsenal", match.Home.Team.Name);
        Assert.Equal("Hull City", match.Away.Team.Name);
    }

    [Fact]
    public void Parses_telegram_message_reply_and_button_press()
    {
        using var doc = JsonDocument.Parse("""
            [
              { "update_id": 10, "message": { "message_id": 5, "from": { "id": 42 }, "chat": { "id": 42 }, "text": "Nội dung mới", "reply_to_message": { "message_id": 3 } } },
              { "update_id": 11, "callback_query": { "id": "cb1", "from": { "id": 42 }, "data": "p|7", "message": { "message_id": 3, "chat": { "id": 42 } } } }
            ]
            """);

        var updates = TelegramUpdate.ParseList(doc.RootElement);

        Assert.Equal(new TelegramMessage(5, 42, 42, "Nội dung mới", 3), updates[0].Message);
        Assert.Equal(new TelegramCallback("cb1", 42, 42, 3, "p|7"), updates[1].Callback);
    }

    [Fact]
    public void Live_card_escapes_text_and_draws_custom_lines_instead_of_the_score()
    {
        var html = new LiveCardHtml("Matchday Challenge", "", "");
        var home = new Team("359", "Arsenal", null);
        var away = new Team("306", "Hull City", null);

        var normal = html.Build(new LiveCard("NGOẠI HẠNG ANH", home, away, "Arsenal", "Hull <City>", 1, 0, "BÀN THẮNG!", "Rice 23'", null, "#111", "#222"));
        Assert.Contains("Hull &lt;City&gt;", normal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("BÀN THẮNG!"), normal);
        Assert.Contains("linear-gradient(135deg, #111 0%, #222 100%)", normal);

        var custom = html.Build(new LiveCard("NGOẠI HẠNG ANH", home, away, "Arsenal", "Hull City", 1, 0, "BÀN THẮNG!", null, ["VÀOOO!"], "#111", "#222"));
        Assert.Contains(System.Net.WebUtility.HtmlEncode("VÀOOO!"), custom);
        Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode("BÀN THẮNG!"), custom);
    }

    [Fact]
    public void Live_card_lists_scorers_under_each_team_with_short_names_ready()
    {
        var html = new LiveCardHtml("Matchday Challenge", "", "");
        var home = new Team("359", "Arsenal", null);
        var away = new Team("306", "Hull City", null);

        var page = html.Build(new LiveCard("NGOẠI HẠNG ANH", home, away, "Arsenal", "Hull City", 1, 0, "KẾT THÚC", null, null, "#111", "#222",
            ["Martin Ødegaard 90'+6' (pen)"], null));

        Assert.Contains("<span class=\"player\" data-short=\"M. &#216;degaard|&#216;degaard\">Martin &#216;degaard</span> 90&#39;+6&#39; (pen)", page);
        Assert.Equal(1, page.Split("class=\"events\"").Length - 1);

        var card = html.Build(new LiveCard("NGOẠI HẠNG ANH", home, away, "Arsenal", "Hull City", 1, 0, "KẾT THÚC", null, null, "#111", "#222",
            null, [$"Smith 61' {Draft.RedCardMark}"]));
        Assert.Contains("61&#39;<i class=\"red-card\"></i></p>", card);
        Assert.DoesNotContain(Draft.RedCardMark, card);
    }
}

public class LiveWatchLimitTests
{
    [Fact]
    public void Watch_limit_is_three_hours_or_three_and_three_quarters_with_extra_time()
    {
        var config = Core.Live.LiveConfig.Load(Path.Combine(TestData.Root, "config", "live.json"));
        var kickoff = new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
        var watch = new WatchedMatch { KickoffUtc = kickoff };

        Assert.Equal(kickoff.AddHours(3), config.WatchDeadline(watch));

        watch.HadExtraTime = true;
        Assert.Equal(kickoff.AddMinutes(225), config.WatchDeadline(watch));
    }
}

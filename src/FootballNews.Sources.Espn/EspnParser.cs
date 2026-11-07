using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FootballNews.Core.Models;
using Match = FootballNews.Core.Models.Match;

namespace FootballNews.Sources.Espn;

/// <summary>Chuyển JSON của ESPN sang model. Thiếu trường bắt buộc thì ném FormatException.</summary>
public static partial class EspnParser
{
    public static IReadOnlyList<Match> ParseScoreboard(string json, Competition competition)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            throw new FormatException($"ESPN {competition.Slug}: không có mảng events");

        return events.EnumerateArray()
            .Select(e => ParseCompetition(
                Required(e, "id").GetString()!,
                Required(e, "competitions")[0],
                competition,
                SeasonYear(e)))
            .ToList();
    }

    public static Match ParseSummary(string json, string matchId, Competition competition)
    {
        using var doc = JsonDocument.Parse(json);
        var header = Required(doc.RootElement, "header");
        return ParseCompetition(matchId, Required(header, "competitions")[0], competition, SeasonYear(header));
    }

    /// <summary>
    /// Lịch thi đấu của một đội (<c>teams/{id}/schedule</c>): chỉ lấy trận đã đá xong, có tỉ số.
    /// Trận phân thắng thua bằng luân lưu lấy kết quả cuối cùng.
    /// </summary>
    public static IReadOnlyList<TeamResult> ParseTeamSchedule(string json, string teamId, int? season)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            throw new FormatException($"ESPN lịch đội {teamId}: không có mảng events");

        var results = new List<TeamResult>();
        foreach (var e in events.EnumerateArray())
        {
            if (season is not null && SeasonYear(e) is { } year && year != season)
                continue;
            var c = Required(e, "competitions")[0];
            var type = Required(Required(c, "status"), "type");
            if (!Flag(type, "completed") || (type.TryGetProperty("state", out var state) ? state.GetString() : null) != "post")
                continue;

            var competitors = Required(c, "competitors").EnumerateArray().ToList();
            var self = competitors.FirstOrDefault(x => Required(Required(x, "team"), "id").GetString() == teamId);
            var other = competitors.FirstOrDefault(x => Required(Required(x, "team"), "id").GetString() != teamId);
            if (self.ValueKind == JsonValueKind.Undefined || other.ValueKind == JsonValueKind.Undefined
                || ReadScore(self) is not { } mine || ReadScore(other) is not { } theirs)
                continue;

            var kickoff = DateTimeOffset.Parse(
                Required(e, "date").GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            results.Add(new TeamResult(kickoff, Outcome(mine, theirs, type, self, other)));
        }
        return results;
    }

    /// <summary>
    /// Lấy kết quả cuối cùng: hoà rồi đá luân lưu thì đội thắng luân lưu là W.
    /// Lịch đội không có tỉ số luân lưu, chỉ có trạng thái STATUS_FINAL_PEN và cờ "winner".
    /// Chỉ dùng "winner" khi có luân lưu: ở lượt về cúp, cờ này có thể đánh dấu đội đi tiếp nhờ tổng tỉ số dù trận chỉ hoà.
    /// </summary>
    private static char Outcome(int mine, int theirs, JsonElement statusType, JsonElement self, JsonElement other)
    {
        if (mine != theirs)
            return mine > theirs ? 'W' : 'L';

        var name = statusType.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (!name.Contains("PEN"))
            return 'D';
        if (ReadInt(self, "shootoutScore") is { } myPens && ReadInt(other, "shootoutScore") is { } theirPens && myPens != theirPens)
            return myPens > theirPens ? 'W' : 'L';
        return Flag(self, "winner") ? 'W' : Flag(other, "winner") ? 'L' : 'D';
    }

    private static int? SeasonYear(JsonElement e) =>
        e.TryGetProperty("season", out var s) && s.ValueKind == JsonValueKind.Object
            && s.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number
            ? y.GetInt32()
            : null;

    // Lịch đội trả score dạng object { value, displayValue }; scoreboard trả chuỗi.
    private static int? ReadScore(JsonElement competitor) =>
        competitor.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Object
            ? ReadInt(score, "value")
            : ReadInt(competitor, "score");

    private static Match ParseCompetition(string id, JsonElement c, Competition competition, int? season)
    {
        var statusElement = Required(c, "status");
        var statusType = Required(statusElement, "type");
        var status = ParseStatus(statusType);
        var competitors = Required(c, "competitors").EnumerateArray().ToList();
        var home = competitors.FirstOrDefault(x => Required(x, "homeAway").GetString() == "home");
        var away = competitors.FirstOrDefault(x => Required(x, "homeAway").GetString() == "away");
        if (home.ValueKind == JsonValueKind.Undefined || away.ValueKind == JsonValueKind.Undefined)
            throw new FormatException($"ESPN trận {id}: thiếu đội nhà hoặc đội khách");

        var kickoff = DateTimeOffset.Parse(
            Required(c, "date").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        var events = c.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? details.EnumerateArray().Select(ParseEvent).OfType<MatchEvent>().ToList()
            : [];

        var statusName = statusType.TryGetProperty("name", out var n) ? n.GetString() : null;
        return new Match(id, competition, kickoff, status, ParseSide(home, status), ParseSide(away, status), events, ParseVenue(c), season,
            statusName, ReadInt(statusElement, "period"));
    }

    // Không bắt buộc: thiếu tên sân thì ảnh chỉ không ghi sân.
    private static string? ParseVenue(JsonElement c) =>
        c.TryGetProperty("venue", out var venue)
            && venue.ValueKind == JsonValueKind.Object
            && venue.TryGetProperty("fullName", out var name)
            && name.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()
            : null;

    private static MatchStatus ParseStatus(JsonElement type)
    {
        var name = type.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (name.Contains("POSTPONED") || name.Contains("SUSPENDED"))
            return MatchStatus.Postponed;
        if (name.Contains("CANCELED") || name.Contains("CANCELLED") || name.Contains("ABANDONED"))
            return MatchStatus.Cancelled;

        return Required(type, "state").GetString() switch
        {
            "pre" => MatchStatus.Scheduled,
            "in" => MatchStatus.Live,
            "post" when Flag(type, "completed") => MatchStatus.Finished,
            "post" => MatchStatus.Postponed,
            var other => throw new FormatException($"ESPN: trạng thái lạ '{other}'"),
        };
    }

    private static MatchSide ParseSide(JsonElement competitor, MatchStatus status)
    {
        var team = Required(competitor, "team");
        var logo = team.TryGetProperty("logo", out var l)
            ? l.GetString()
            : team.TryGetProperty("logos", out var logos) && logos.GetArrayLength() > 0
                ? logos[0].GetProperty("href").GetString()
                : null;

        // ESPN trả score "0" cho cả trận chưa đá.
        var hasScore = status is MatchStatus.Finished or MatchStatus.Live;
        return new MatchSide(
            new Team(Required(team, "id").GetString()!, Required(team, "displayName").GetString()!, logo),
            hasScore ? ReadInt(competitor, "score") : null,
            hasScore ? ReadInt(competitor, "shootoutScore") : null,
            // Không dùng "form" của ESPN: chuỗi đó trộn nhiều giải và xếp mới đến cũ. Xem TeamForm.
            null);
    }

    private static MatchEvent? ParseEvent(JsonElement d)
    {
        // Loạt luân lưu đã nằm trong shootoutScore, không tính là bàn thắng.
        if (Flag(d, "shootout"))
            return null;

        MatchEventType? type =
            Flag(d, "redCard") ? MatchEventType.RedCard :
            Flag(d, "yellowCard") ? MatchEventType.YellowCard :
            !Flag(d, "scoringPlay") ? null :
            Flag(d, "ownGoal") ? MatchEventType.OwnGoal :
            Flag(d, "penaltyKick") ? MatchEventType.PenaltyGoal :
            MatchEventType.Goal;
        if (type is null)
            return null;

        var minute = d.TryGetProperty("clock", out var clock) && clock.TryGetProperty("displayValue", out var v)
            ? v.GetString() ?? ""
            : "";
        var teamId = d.TryGetProperty("team", out var t) && t.TryGetProperty("id", out var tid) ? tid.GetString() ?? "" : "";
        return new MatchEvent(type.Value, minute, PlayerName(d), teamId);
    }

    /// <summary>"K. Havertz" thành "Havertz"; "K. De Bruyne" thành "De Bruyne".</summary>
    private static string PlayerName(JsonElement detail)
    {
        if (!detail.TryGetProperty("athletesInvolved", out var athletes) || athletes.GetArrayLength() == 0)
            return "";
        var athlete = athletes[0];
        if (athlete.TryGetProperty("shortName", out var s) && s.GetString() is { Length: > 0 } shortName)
            return InitialPrefix().Replace(shortName, "");
        return athlete.TryGetProperty("displayName", out var d) ? d.GetString() ?? "" : "";
    }

    private static int? ReadInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => (int)value.GetDouble(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static JsonElement Required(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v : throw new FormatException($"ESPN: thiếu trường '{name}'");

    [GeneratedRegex(@"^\p{Lu}\.\s+")]
    private static partial Regex InitialPrefix();
}

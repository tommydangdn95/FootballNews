namespace FootballNews.Core.Models;

/// <param name="Zone">Vùng xếp hạng theo ESPN, vd <c>Champions League</c>, <c>Relegation</c>. Đội không thuộc vùng nào thì null.</param>
/// <param name="Form">Phong độ do <see cref="TeamForm"/> tính, xếp cũ đến mới. Chỉ có ở bài hằng ngày.</param>
/// <param name="JustPlayed">Đội có trận trong bài kết quả đi kèm, để tô nổi trên bảng.</param>
/// <param name="RankChange">Số bậc lên (dương) hoặc xuống (âm) so với trước các trận trong bài kết quả. 0 hoặc null thì không vẽ.</param>
public sealed record StandingRow(
    int Rank,
    Team Team,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    string? Zone,
    string? Form = null,
    bool JustPlayed = false,
    int? RankChange = null)
{
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

/// <summary>Bảng xếp hạng của một giải tại lúc lấy, xếp theo thứ hạng.</summary>
/// <param name="Round">Lượt trận vừa đá, để ghi trên ảnh. Chỉ có ở bài hằng ngày.</param>
public sealed record Standings(Competition Competition, IReadOnlyList<StandingRow> Rows, int? Round = null)
{
    /// <summary>
    /// Chỉ giữ vùng có được nhờ thứ hạng. ESPN còn ghi vùng cho đội có suất châu Âu theo đường khác
    /// (vd vô địch cúp quốc gia), như Racing Santander hạng 10 mang "Europa League" dù hạng 8–9 không có vùng nào.
    /// Vùng xuống hạng hoặc bị loại phải nối liền từ cuối bảng lên; các vùng khác phải nối liền từ hạng 1 xuống.
    /// </summary>
    public static IReadOnlyList<StandingRow> PositionalZones(IReadOnlyList<StandingRow> rows)
    {
        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count && rows[i].Zone is { } zone && !IsBottomZone(zone); i++)
            keep[i] = true;
        for (var i = rows.Count - 1; i >= 0 && rows[i].Zone is { } zone && IsBottomZone(zone); i--)
            keep[i] = true;
        return rows.Select((r, i) => keep[i] || r.Zone is null ? r : r with { Zone = null }).ToList();
    }

    private static bool IsBottomZone(string zone) =>
        zone.Contains("relegat", StringComparison.OrdinalIgnoreCase) || zone.Contains("eliminated", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Thứ hạng thay đổi thế nào sau <paramref name="results"/>: id đội → số bậc lên (dương) hoặc xuống (âm).
    /// ESPN có trường <c>rankChange</c> nhưng luôn bằng 0, nên tự dựng bảng trước các trận đó bằng cách trừ kết quả
    /// của chúng ra rồi xếp lại theo điểm, hiệu số, bàn thắng. Đội bằng nhau cả ba thì giữ thứ tự hiện tại của ESPN,
    /// vì luật phụ (đối đầu, fair-play...) mỗi giải một kiểu. Trận chưa xong hoặc thiếu tỉ số thì bỏ qua.
    /// </summary>
    public IReadOnlyDictionary<string, int> RankChanges(IEnumerable<Match> results)
    {
        var before = Rows.ToDictionary(r => r.Team.Id, r => (r.Rank, r.Points, r.GoalsFor, r.GoalsAgainst));
        foreach (var m in results)
        {
            if (m.Status != MatchStatus.Finished || m.Home.Score is not { } home || m.Away.Score is not { } away)
                continue;
            Undo(m.Home.Team.Id, home, away);
            Undo(m.Away.Team.Id, away, home);
        }

        var previous = before
            .OrderByDescending(t => t.Value.Points)
            .ThenByDescending(t => t.Value.GoalsFor - t.Value.GoalsAgainst)
            .ThenByDescending(t => t.Value.GoalsFor)
            .ThenBy(t => t.Value.Rank)
            .Select((t, i) => (t.Key, Rank: i + 1))
            .ToDictionary(t => t.Key, t => t.Rank);
        return Rows.GroupBy(r => r.Team.Id).ToDictionary(g => g.Key, g => previous[g.Key] - g.First().Rank);

        void Undo(string team, int scored, int conceded)
        {
            if (!before.TryGetValue(team, out var t))
                return;
            var points = scored > conceded ? 3 : scored == conceded ? 1 : 0;
            before[team] = (t.Rank, t.Points - points, t.GoalsFor - scored, t.GoalsAgainst - conceded);
        }
    }

    /// <summary>
    /// Bảng để vẽ sau bài kết quả: mũi tên lên/xuống hạng theo <paramref name="results"/> (các trận của giải này trong bài),
    /// tô nổi đội vừa đá nếu <paramref name="highlightPlayed"/>. Vòng ghi trên ảnh là vòng lớn nhất trong bài; giải không có
    /// trận nào trong bài (chạy force) thì lấy số trận đã đá nhiều nhất trên bảng, cùng quy tắc với <see cref="Matchday"/>.
    /// </summary>
    public Standings AfterResults(IReadOnlyList<Match> results, bool highlightPlayed)
    {
        var changes = RankChanges(results);
        var played = highlightPlayed
            ? results.SelectMany(m => new[] { m.Home.Team.Id, m.Away.Team.Id }).ToHashSet()
            : [];
        var round = results.Select(m => m.Round).Max() ?? (Rows.Count > 0 ? Rows.Max(r => r.Played) : null);
        return this with
        {
            Rows = Rows.Select(r => r with { JustPlayed = played.Contains(r.Team.Id), RankChange = changes.GetValueOrDefault(r.Team.Id) }).ToList(),
            Round = round,
        };
    }

    /// <summary>Id đội → số trận đã đá, dùng cho <see cref="Matchday.Assign"/>.</summary>
    public IReadOnlyDictionary<string, int> GamesPlayed() => Rows
        .GroupBy(r => r.Team.Id)
        .ToDictionary(g => g.Key, g => g.First().Played);
}

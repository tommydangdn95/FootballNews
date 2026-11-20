namespace FootballNews.Core.Models;

/// <param name="Zone">Vùng xếp hạng theo ESPN, vd <c>Champions League</c>, <c>Relegation</c>. Đội không thuộc vùng nào thì null.</param>
/// <param name="Form">Phong độ do <see cref="TeamForm"/> tính, xếp cũ đến mới. Chỉ có ở bài hằng ngày.</param>
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
    string? Form = null)
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

    /// <summary>Id đội → số trận đã đá, dùng cho <see cref="Matchday.Assign"/>.</summary>
    public IReadOnlyDictionary<string, int> GamesPlayed() => Rows
        .GroupBy(r => r.Team.Id)
        .ToDictionary(g => g.Key, g => g.First().Played);
}

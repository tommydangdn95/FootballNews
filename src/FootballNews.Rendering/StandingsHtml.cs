using System.Globalization;
using System.Net;
using System.Text;
using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>
/// Dựng HTML ảnh bảng xếp hạng, khung 1080×1350: đầu ảnh có logo và tên giải, logo trang ở góc phải, vòng đấu;
/// bên dưới là bảng nền trắng với thanh tiêu đề mang màu của giải. Mỗi giải ảnh riêng; bảng dài
/// (vòng phân hạng cúp châu Âu) chia đều nhiều ảnh.
/// </summary>
public sealed class StandingsHtml(ClubCatalog clubs, CardTheme baseTheme, string boldFontBase64, string mediumFontBase64, string pageLogoBase64 = "")
{
    /// <summary>Số đội tối đa trên một ảnh: giải 20 đội vừa một ảnh, bảng 36 đội thành hai ảnh 18 đội.</summary>
    public const int MaxRowsPerPage = 20;

    // Màu vùng xếp hạng: ESPN có màu riêng nhưng nhạt và đôi khi sai cú pháp (vd "##B5E7CE"), nên dùng màu cố định theo loại vùng.
    private const string OtherZone = "#9AA5B1";
    private static readonly Dictionary<string, string> ZoneColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Champions League"] = "#2E7DFF",
        ["Qualifies for round of 16"] = "#2E7DFF",
        ["Champions League qualifying"] = "#7FB2FF",
        ["Knockout phase playoffs - seeded"] = "#19C37D",
        ["Knockout phase playoffs - unseeded"] = "#A6E3C4",
        ["Europa League"] = "#FF8A1F",
        ["Europa League qualifying"] = "#FFC27A",
        ["Conference League"] = "#19C37D",
        ["Conference League qualifying"] = "#19C37D",
        ["Promotion"] = "#19C37D",
        ["Relegation playoff"] = "#FFB020",
        ["Relegation"] = "#E5383B",
        ["Relegated"] = "#E5383B",
        ["Eliminated"] = "#E5383B",
    };

    public static string ZoneColor(string zone) => ZoneColors.GetValueOrDefault(zone, OtherZone);

    public IReadOnlyList<string> BuildPages(Standings table, CardLocale locale) =>
        Split(table.Rows).Select(rows => BuildPage(table, rows, locale)).ToList();

    // Chia đều thay vì lấp đầy từng ảnh, như ảnh lịch đấu.
    private static List<StandingRow[]> Split(IReadOnlyList<StandingRow> rows)
    {
        var pageCount = Math.Max(1, (rows.Count + MaxRowsPerPage - 1) / MaxRowsPerPage);
        var pages = new List<StandingRow[]>();
        var start = 0;
        for (var page = 0; page < pageCount; page++)
        {
            var size = rows.Count / pageCount + (page < rows.Count % pageCount ? 1 : 0);
            pages.Add(rows.Skip(start).Take(size).ToArray());
            start += size;
        }
        return pages;
    }

    private string BuildPage(Standings table, StandingRow[] rows, CardLocale locale)
    {
        var competition = table.Competition;
        var theme = baseTheme.For(competition.Slug);
        var sb = new StringBuilder();
        sb.Append($"<!DOCTYPE html><html lang=\"{locale.Code}\"><head><meta charset=\"utf-8\"><style>").Append(Css(theme)).Append("</style></head><body><div class=\"card\">");

        sb.Append("<header>");
        if (!string.IsNullOrEmpty(competition.Logo))
            sb.Append($"<img class=\"league-logo\" src=\"{E(competition.Logo)}\" alt=\"\" onerror=\"this.remove()\">");
        sb.Append($"<div class=\"heading\"><h1>{E(locale.CompetitionName(competition))}</h1>");
        var subtitle = table.Round is { } round ? $"{locale.StandingsTitle} · {locale.StandingsRound(round)}" : locale.StandingsTitle;
        sb.Append($"<p>{E(subtitle)}</p></div>");
        sb.Append("</header>");
        if (pageLogoBase64.Length > 0)
            sb.Append($"<img class=\"page-logo\" src=\"data:image/png;base64,{pageLogoBase64}\" alt=\"\">");

        sb.Append("<main><div class=\"row head\">");
        var columns = locale.StandingsColumns;
        for (var i = 0; i < columns.Count; i++)
            sb.Append($"<span class=\"{ColumnClass(i, columns.Count)}\">{E(columns[i])}</span>");
        sb.Append("</div><div class=\"rows\">");
        for (var i = 0; i < rows.Length; i++)
        {
            // Các đội cùng vùng liền nhau dùng chung một dải màu: chỉ bo tròn ở đầu và cuối dải.
            var zone = rows[i].Zone;
            var starts = i == 0 || !SameZone(rows[i - 1].Zone, zone);
            var ends = i == rows.Length - 1 || !SameZone(rows[i + 1].Zone, zone);
            sb.Append(Row(rows[i], starts, ends));
        }
        sb.Append("</div></main>");

        // Chú thích chỉ ghi các vùng có trên ảnh này, theo thứ tự xuất hiện.
        var zones = rows.Select(r => r.Zone).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        sb.Append("<footer><div class=\"legend\">");
        foreach (var zone in zones)
            sb.Append($"<span><i style=\"background: {ZoneColor(zone)}\"></i>{E(locale.Zone(zone))}</span>");
        sb.Append("</div>");
        var note = rows.Any(r => !string.IsNullOrEmpty(r.Form)) ? E(locale.FormNote(competition)) : "";
        sb.Append($"<div class=\"meta\"><span>{note}</span><span>{E(theme.PageName)}</span></div></footer>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    // Cột 0 là hạng, cột 1 là đội, hai cột cuối là điểm và phong độ; còn lại là số.
    private static string ColumnClass(int index, int count) =>
        index == 0 ? "pos" : index == 1 ? "club" : index == count - 1 ? "form" : index == count - 2 ? "pts" : "num";

    private static bool SameZone(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string Row(StandingRow r, bool zoneStarts, bool zoneEnds)
    {
        var bar = r.Zone is null
            ? ""
            : $"<i class=\"zone{(zoneStarts ? " start" : "")}{(zoneEnds ? " end" : "")}\" style=\"background: {ZoneColor(r.Zone)}\"></i>";
        var gd = r.GoalDifference > 0 ? $"+{r.GoalDifference}" : r.GoalDifference.ToString(CultureInfo.InvariantCulture);
        return $"""
            <div class="row{(r.JustPlayed ? " played" : "")}">
              {bar}<span class="pos">{r.Rank}{Move(r.RankChange)}</span>
              <span class="club">{Logo(r.Team)}<span class="name">{E(clubs.DisplayName(r.Team))}</span></span>
              <span class="num">{r.Played}</span><span class="num">{r.Won}</span><span class="num">{r.Drawn}</span><span class="num">{r.Lost}</span>
              <span class="num">{gd}</span><span class="pts">{r.Points}</span>{Form(r.Form)}
            </div>
            """;
    }

    // Tam giác xanh hướng lên khi đội lên hạng, đỏ hướng xuống khi tụt hạng; giữ nguyên thì không vẽ.
    private static string Move(int? change) => change switch
    {
        > 0 => "<i class=\"move up\"></i>",
        < 0 => "<i class=\"move down\"></i>",
        _ => "",
    };

    // Cùng kiểu ô phong độ với ảnh lịch đấu: trận gần nhất bên phải.
    private static string Form(string? form)
    {
        var chips = (form ?? "").Select(c => c switch
        {
            'W' => "<b class=\"w\">W</b>",
            'D' => "<b class=\"d\">D</b>",
            'L' => "<b class=\"l\">L</b>",
            _ => "",
        });
        return $"<span class=\"form\">{string.Concat(chips)}</span>";
    }

    private static string Logo(Team team) => string.IsNullOrEmpty(team.LogoUrl)
        ? "<span class=\"logo\"></span>"
        : $"<span class=\"logo\"><img src=\"{E(team.LogoUrl)}\" alt=\"\" onerror=\"this.remove()\"></span>";

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private string Css(CardTheme theme) => $$"""
        @font-face { font-family: BVP; font-weight: 700; src: url(data:font/ttf;base64,{{boldFontBase64}}); }
        @font-face { font-family: BVP; font-weight: 500; src: url(data:font/ttf;base64,{{mediumFontBase64}}); }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { width: 1080px; font-family: BVP, sans-serif; font-weight: 500; background: {{theme.Background}}; }
        .card { width: 1080px; height: 1350px; display: flex; flex-direction: column; padding: 36px 44px 30px; color: {{theme.Text}};
                background: linear-gradient(160deg, {{theme.Background}} 35%, color-mix(in srgb, {{theme.Background}} 70%, {{theme.Accent}}) 100%); }
        .card { position: relative; }
        header { display: flex; align-items: center; justify-content: center; gap: 24px; height: 130px; margin: 0 96px 24px; }
        .league-logo { height: 112px; max-width: 150px; object-fit: contain; }
        .heading { text-align: left; }
        h1 { font-size: 44px; font-weight: 700; line-height: 1.1; }
        header p { font-size: 21px; font-weight: 700; letter-spacing: 2px; color: {{theme.Accent}}; margin-top: 6px; }
        .page-logo { position: absolute; right: 24px; top: 20px; height: 84px; }
        main { flex: 1; min-height: 0; display: flex; flex-direction: column; background: #FFFFFF; color: {{theme.Background}}; border-radius: 28px; overflow: hidden; }
        .row { display: grid; grid-template-columns: 96px 1fr 54px 54px 54px 54px 66px 62px 172px; align-items: center; padding: 0 16px 0 0; }
        .row.head { height: 58px; flex: none; font-size: 18px; font-weight: 700; letter-spacing: 1px;
                    background: linear-gradient(90deg, {{theme.Surface}}, {{theme.SurfaceEnd}}); color: {{theme.SurfaceText}}; }
        .rows { flex: 1; min-height: 0; display: flex; flex-direction: column; }
        .rows .row { position: relative; flex: 1; font-size: 24px; border-bottom: 1px solid color-mix(in srgb, {{theme.Background}} 9%, transparent); }
        .rows .row:last-child { border-bottom: none; }
        /* Đội vừa đá: nền nhạt theo dải màu của giải, cùng màu với thanh tiêu đề bảng. */
        .rows .row.played { background: linear-gradient(90deg, color-mix(in srgb, {{theme.Surface}} 55%, #FFFFFF), color-mix(in srgb, {{theme.SurfaceEnd}} 55%, #FFFFFF)); }
        /* Ô phong độ cuối của đội vừa đá chính là trận hôm nay: viền đậm để nối với tỉ số trong bài kết quả. */
        .rows .row.played .form b:last-child { box-shadow: 0 0 0 3px #FFFFFF, 0 0 0 5px {{theme.Background}}; }
        .pos { text-align: center; font-weight: 700; position: relative; }
        .move { position: absolute; right: 8px; top: 50%; width: 0; height: 0; border-left: 8px solid transparent; border-right: 8px solid transparent; }
        .move.up { margin-top: -6px; border-bottom: 12px solid #1E9E5A; }
        .move.down { margin-top: -6px; border-top: 12px solid #D2433D; }
        .zone { position: absolute; left: 0; top: 0; bottom: -1px; width: 9px; }
        .zone.end { bottom: 0; }
        .club { display: flex; align-items: center; gap: 16px; min-width: 0; }
        .club { gap: 14px; }
        .logo { flex: none; width: 36px; height: 36px; display: flex; align-items: center; justify-content: center; }
        .logo img { width: 36px; height: 36px; object-fit: contain; }
        .name { font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .num { text-align: center; }
        .rows .num { color: color-mix(in srgb, {{theme.Background}} 80%, transparent); }
        .pts { text-align: center; font-weight: 700; }
        .form { display: flex; gap: 5px; justify-content: center; }
        .form b { width: 28px; height: 28px; border-radius: 6px; display: inline-flex; align-items: center; justify-content: center; font-size: 15px; font-weight: 700; color: #fff; }
        .form .w { background: #1E9E5A; } .form .d { background: #75807B; } .form .l { background: #D2433D; }
        footer { margin-top: 16px; font-size: 20px; color: {{theme.Muted}}; }
        .legend { display: flex; flex-wrap: wrap; gap: 6px 26px; }
        .legend:empty { display: none; }
        .legend span { display: inline-flex; align-items: center; gap: 8px; }
        .legend i { width: 16px; height: 16px; border-radius: 4px; display: inline-block; }
        .meta { display: flex; justify-content: space-between; gap: 24px; margin-top: 8px; }
        .meta span:last-child { font-weight: 700; white-space: nowrap; }
        .meta span:first-child { font-size: 17px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; align-self: center; }
        """;
}

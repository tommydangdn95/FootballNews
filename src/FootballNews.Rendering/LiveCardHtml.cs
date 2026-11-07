using System.Net;
using System.Text;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>Nội dung một ảnh live. Có <see cref="CustomLines"/> thì chỉ vẽ các dòng đó thay cho tiêu đề, tỉ số và chi tiết.</summary>
public sealed record LiveCard(
    string Competition,
    Team Home,
    Team Away,
    string HomeName,
    string AwayName,
    int HomeScore,
    int AwayScore,
    string Title,
    string? Detail,
    IReadOnlyList<string>? CustomLines,
    string GradientFrom,
    string GradientTo);

/// <summary>
/// Ảnh vuông 1080×1080 nền chuyển màu, chữ trắng ở giữa, giống kiểu bài chữ nền màu của Facebook.
/// Không dùng emoji trên ảnh để không phụ thuộc font của máy chạy.
/// </summary>
public sealed class LiveCardHtml(string pageName, string boldFontBase64, string mediumFontBase64, string pageLogoBase64 = "")
{
    public string Build(LiveCard card)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><style>").Append(Css(card)).Append("</style></head><body>");
        sb.Append("<div class=\"card\">");
        sb.Append($"<div class=\"competition\">{E(card.Competition)}</div>");
        sb.Append("<main>");

        if (card.CustomLines is { Count: > 0 } lines)
        {
            var size = CustomFontSize(lines);
            sb.Append($"<div class=\"custom\" style=\"font-size:{size}px\">");
            foreach (var line in lines)
                sb.Append($"<p>{E(line)}</p>");
            sb.Append("</div>");
        }
        else
        {
            sb.Append($"<div class=\"title\">{E(card.Title)}</div>");
            sb.Append("<div class=\"teams\">");
            sb.Append(TeamBlock(card.Home, card.HomeName));
            sb.Append($"<div class=\"score\">{card.HomeScore}<span>-</span>{card.AwayScore}</div>");
            sb.Append(TeamBlock(card.Away, card.AwayName));
            sb.Append("</div>");
            if (!string.IsNullOrWhiteSpace(card.Detail))
                sb.Append($"<div class=\"detail\">{E(card.Detail)}</div>");
        }

        sb.Append("</main><footer>");
        if (pageLogoBase64.Length > 0)
            sb.Append($"<img class=\"page-logo\" src=\"data:image/png;base64,{pageLogoBase64}\" alt=\"\">");
        sb.Append($"<span>{E(pageName)}</span></footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>Dòng càng dài, càng nhiều dòng thì chữ càng nhỏ, để vừa khung 1080px.</summary>
    public static int CustomFontSize(IReadOnlyList<string> lines)
    {
        var longest = lines.Max(l => l.Length);
        var byWidth = longest <= 14 ? 96 : longest <= 20 ? 80 : longest <= 28 ? 64 : longest <= 40 ? 52 : 42;
        var byHeight = lines.Count <= 3 ? 96 : lines.Count <= 5 ? 64 : 48;
        return Math.Min(byWidth, byHeight);
    }

    private static string TeamBlock(Team team, string name)
    {
        var logo = string.IsNullOrEmpty(team.LogoUrl)
            ? ""
            : $"<img src=\"{E(team.LogoUrl)}\" alt=\"\" onerror=\"this.remove()\">";
        return $"<div class=\"team\">{logo}<div class=\"name\">{E(name)}</div></div>";
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private string Css(LiveCard card) => $$"""
        @font-face { font-family: BVP; font-weight: 700; src: url(data:font/ttf;base64,{{boldFontBase64}}); }
        @font-face { font-family: BVP; font-weight: 500; src: url(data:font/ttf;base64,{{mediumFontBase64}}); }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { width: 1080px; height: 1080px; font-family: BVP, sans-serif; font-weight: 700; color: #FFFFFF; }
        .card { width: 1080px; height: 1080px; padding: 64px 56px 48px; display: flex; flex-direction: column;
                background: linear-gradient(135deg, {{card.GradientFrom}} 0%, {{card.GradientTo}} 100%); }
        .competition { font-size: 30px; letter-spacing: 4px; text-align: center; opacity: .85; }
        main { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 44px; text-align: center; }
        .title { font-size: 92px; line-height: 1.1; letter-spacing: 2px; text-shadow: 0 6px 24px rgba(0,0,0,.25); }
        .teams { width: 100%; display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; column-gap: 24px; }
        .team { display: flex; flex-direction: column; align-items: center; gap: 18px; min-width: 0; }
        .team img { width: 150px; height: 150px; object-fit: contain; filter: drop-shadow(0 6px 16px rgba(0,0,0,.3)); }
        .name { font-size: 44px; line-height: 1.15; }
        .score { font-size: 150px; line-height: 1; white-space: nowrap; text-shadow: 0 8px 28px rgba(0,0,0,.3); }
        .score span { margin: 0 24px; opacity: .8; }
        .detail { font-size: 50px; font-weight: 500; line-height: 1.25; background: rgba(0,0,0,.22); border-radius: 28px; padding: 16px 40px; }
        .custom { line-height: 1.25; text-shadow: 0 6px 24px rgba(0,0,0,.25); }
        .custom p + p { margin-top: 16px; }
        footer { display: flex; align-items: center; justify-content: center; gap: 18px; font-size: 30px; opacity: .9; }
        .page-logo { height: 64px; }
        """;
}

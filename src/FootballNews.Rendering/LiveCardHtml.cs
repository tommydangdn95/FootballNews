using System.Net;
using System.Text;
using FootballNews.Core.Live;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>
/// Nội dung một ảnh live. Có <see cref="CustomLines"/> thì chỉ vẽ các dòng đó thay cho tiêu đề, tỉ số và chi tiết.
/// <see cref="HomeLines"/>, <see cref="AwayLines"/>: các dòng nhỏ dưới tên mỗi đội (người ghi bàn, thẻ đỏ).
/// <see cref="RedCardIcon"/>: bài thẻ đỏ. Vẽ thẻ đỏ lớn cạnh tiêu đề, không có tỉ số; chỉ logo và tên đội
/// <see cref="TeamId"/> cùng dòng chi tiết (cầu thủ, phút).
/// </summary>
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
    string GradientTo,
    IReadOnlyList<string>? HomeLines = null,
    IReadOnlyList<string>? AwayLines = null,
    bool RedCardIcon = false,
    string? TeamId = null);

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
        else if (card.RedCardIcon)
        {
            // Logo đội là điểm nhấn của ảnh nên tiêu đề nhỏ hơn các ảnh khác.
            sb.Append($"<div class=\"title\" style=\"font-size:{Math.Min(TitleFontSize(card.Title), 56)}px\"><i class=\"red-card big\"></i>{E(card.Title)}</div>");
            var (team, name) = card.TeamId == card.Home.Id ? (card.Home, card.HomeName)
                : card.TeamId == card.Away.Id ? (card.Away, card.AwayName)
                : (null, null);
            if (team is not null)
            {
                sb.Append("<div class=\"solo\">");
                if (!string.IsNullOrEmpty(team.LogoUrl))
                    sb.Append($"<img src=\"{E(team.LogoUrl)}\" alt=\"\" onerror=\"this.remove()\">");
                sb.Append($"<div class=\"name\">{E(name!)}</div></div>");
            }
            if (!string.IsNullOrWhiteSpace(card.Detail))
                sb.Append($"<div class=\"detail\">{E(card.Detail)}</div>");
        }
        else
        {
            var size = TeamLinesFontSize(Math.Max(card.HomeLines?.Count ?? 0, card.AwayLines?.Count ?? 0));
            sb.Append($"<div class=\"title\" style=\"font-size:{TitleFontSize(card.Title)}px\">{E(card.Title)}</div>");
            sb.Append("<div class=\"teams\">");
            sb.Append(TeamBlock(card.Home, card.HomeName, card.HomeLines, size));
            sb.Append($"<div class=\"score\">{card.HomeScore}<span>-</span>{card.AwayScore}</div>");
            sb.Append(TeamBlock(card.Away, card.AwayName, card.AwayLines, size));
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

    /// <summary>Tiêu đề dài (vd "VAR HỦY BÀN THẮNG CỦA ...") thì chữ nhỏ lại để không chiếm quá nhiều dòng.</summary>
    public static int TitleFontSize(string title) => title.Length <= 18 ? 92 : title.Length <= 28 ? 72 : 54;

    /// <summary>Càng nhiều dòng dưới tên đội thì chữ càng nhỏ, để vừa khung 1080px.</summary>
    public static int TeamLinesFontSize(int lines) => lines <= 3 ? 32 : lines <= 5 ? 28 : 24;

    private static string TeamBlock(Team team, string name, IReadOnlyList<string>? lines, int size)
    {
        var logo = string.IsNullOrEmpty(team.LogoUrl)
            ? ""
            : $"<img src=\"{E(team.LogoUrl)}\" alt=\"\" onerror=\"this.remove()\">";
        var extra = lines is { Count: > 0 }
            ? $"<div class=\"events\" style=\"font-size:{size}px\">{string.Concat(lines.Select(EventLine))}</div>"
            : "";
        return $"<div class=\"team\">{logo}<div class=\"name\">{E(name)}</div>{extra}</div>";
    }

    /// <summary>
    /// Một dòng "Tên 23' (pen)", luôn nằm trên một dòng. Tên kèm sẵn các cách viết tắt trong <c>data-short</c>
    /// để <see cref="FitScript"/> thay khi dòng dài quá cột.
    /// Dòng kết thúc bằng <see cref="Draft.RedCardMark"/> thì vẽ hình thẻ đỏ sau phút.
    /// </summary>
    private static string EventLine(string line)
    {
        var redCard = line.EndsWith(Draft.RedCardMark, StringComparison.Ordinal);
        if (redCard)
            line = line[..^Draft.RedCardMark.Length].TrimEnd();
        var icon = redCard ? "<i class=\"red-card\"></i>" : "";
        var words = line.Split(' ');
        var at = Array.FindIndex(words, w => w.Length > 0 && char.IsDigit(w[0]) && w.Contains('\''));
        if (at <= 0)
            return $"<p>{E(line)}{icon}</p>";
        var name = words[..at];
        var shorts = ShortNames(name);
        var data = shorts.Count > 0 ? $" data-short=\"{E(string.Join('|', shorts))}\"" : "";
        return $"<p><span class=\"player\"{data}>{E(string.Join(' ', name))}</span> {E(string.Join(' ', words[at..]))}{icon}</p>";
    }

    /// <summary>"Martin Ødegaard" → "M. Ødegaard", "Ødegaard"; "Kevin De Bruyne" → "K. De Bruyne", "De Bruyne". Tên một chữ thì không có.</summary>
    public static IReadOnlyList<string> ShortNames(IReadOnlyList<string> name)
    {
        if (name.Count < 2)
            return [];
        var rest = string.Join(' ', name.Skip(1));
        return [$"{char.ToUpper(name[0][0])}. {rest}", rest];
    }

    /// <summary>
    /// Chạy trong trình duyệt sau khi font đã tải: dòng nào dưới tên đội dài quá cột thì thay tên bằng cách viết tắt,
    /// vẫn dài thì thu nhỏ chữ; sau cùng mọi dòng dùng chung cỡ chữ nhỏ nhất.
    /// </summary>
    public const string FitScript = """
        () => {
          const lines = [...document.querySelectorAll('.events p')];
          let smallest = Infinity;
          for (const p of lines) {
            const fits = () => p.scrollWidth <= p.clientWidth;
            const player = p.querySelector('.player');
            const shorts = player?.dataset.short ? player.dataset.short.split('|') : [];
            for (const s of shorts) { if (fits()) break; player.textContent = s; }
            let size = parseFloat(getComputedStyle(p).fontSize);
            while (!fits() && size > 16) { size -= 1; p.style.fontSize = size + 'px'; }
            smallest = Math.min(smallest, size);
          }
          // Mọi dòng của cả hai đội cùng một cỡ chữ cho đều mắt.
          for (const p of lines) p.style.fontSize = smallest + 'px';
          return true;
        }
        """;

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
        .team { display: flex; flex-direction: column; align-items: center; align-self: start; gap: 18px; min-width: 0; }
        .team img { width: 150px; height: 150px; object-fit: contain; filter: drop-shadow(0 6px 16px rgba(0,0,0,.3)); }
        .name { font-size: 44px; line-height: 1.15; }
        .events { align-self: stretch; font-weight: 500; line-height: 1.3; opacity: .92; margin-top: -6px; }
        .events p { white-space: nowrap; }
        .events p + p { margin-top: 4px; }
        .red-card { display: inline-block; width: .62em; height: .85em; margin-left: .35em; vertical-align: -.08em;
                    background: #E10600; border: 2px solid #FFFFFF; border-radius: 3px; transform: rotate(8deg);
                    box-shadow: 0 2px 6px rgba(0,0,0,.35); }
        .red-card.big { width: .62em; height: .86em; margin: 0 .3em 0 0; vertical-align: -.1em;
                        border: 5px solid #FFFFFF; border-radius: 10px; transform: rotate(-10deg); box-shadow: 0 8px 24px rgba(0,0,0,.35); }
        .solo { display: flex; flex-direction: column; align-items: center; gap: 22px; }
        .solo img { width: 260px; height: 260px; object-fit: contain; filter: drop-shadow(0 10px 24px rgba(0,0,0,.3)); }
        .solo .name { font-size: 56px; }
        .score { align-self: start; font-size: 150px; line-height: 1; white-space: nowrap; text-shadow: 0 8px 28px rgba(0,0,0,.3); }
        .score span { margin: 0 24px; opacity: .8; }
        .detail { font-size: 50px; font-weight: 500; line-height: 1.25; background: rgba(0,0,0,.22); border-radius: 28px; padding: 16px 40px; }
        .custom { line-height: 1.25; text-shadow: 0 6px 24px rgba(0,0,0,.25); }
        .custom p + p { margin-top: 16px; }
        footer { display: flex; align-items: center; justify-content: center; gap: 18px; font-size: 30px; opacity: .9; }
        .page-logo { height: 64px; }
        """;
}

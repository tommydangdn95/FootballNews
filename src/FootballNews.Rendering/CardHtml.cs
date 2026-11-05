using System.Globalization;
using System.Net;
using System.Text;
using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Rendering;

/// <summary>Dựng HTML của ảnh bảng tỉ số. Ảnh dùng chữ và CSS, không dùng emoji để không phụ thuộc font của máy chạy.</summary>
public sealed class CardHtml(ClubCatalog clubs, CardTheme theme, string boldFontBase64, string mediumFontBase64, string pageLogoBase64 = "")
{
    /// <summary>Chia digest thành các trang theo từng giải; các trận cập nhật lên đầu.</summary>
    public IReadOnlyList<string> BuildPages(Digest digest, CardLocale locale)
    {
        var entries = digest.Updates.Select(m => (Match: m, IsUpdate: true))
            .Concat(digest.Matches
                .OrderBy(m => m.Competition.Order)
                .ThenBy(m => m.KickoffUtc)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .Select(m => (Match: m, IsUpdate: false)))
            .ToList();

        var dates = (digest.Matches.Count > 0 ? digest.Matches : digest.Updates)
            .Select(m => LocalDate(locale, m))
            .Distinct()
            .Order()
            .ToList();

        var pages = CardPaging.Paginate(entries.GroupBy(e => e.IsUpdate ? null : e.Match.Competition.Slug).Select(g => g.ToList()));
        return pages.Select((page, i) => BuildPage(digest.Kind, locale, dates, page, i + 1, pages.Count)).ToList();
    }

    private string BuildPage(DigestKind kind, CardLocale locale, List<DateOnly> dates, (Match Match, bool IsUpdate)[] entries, int page, int pageCount)
    {
        var isResults = kind == DigestKind.Results;
        var sb = new StringBuilder();
        sb.Append($"<!DOCTYPE html><html lang=\"{locale.Code}\"><head><meta charset=\"utf-8\"><style>").Append(Css()).Append("</style></head><body><div class=\"card\">");
        sb.Append($"<header><div class=\"heading\"><h1>{E(isResults ? locale.ResultsTitle : locale.UpcomingTitle)}</h1>");
        sb.Append($"<div class=\"date\">{E(DateLine(locale, dates))}</div>");
        sb.Append($"<p>{E(isResults ? locale.ResultsSubtitle : locale.UpcomingSubtitle)}</p></div>");
        if (pageLogoBase64.Length > 0)
            sb.Append($"<img class=\"page-logo\" src=\"data:image/png;base64,{pageLogoBase64}\" alt=\"\">");
        sb.Append("</header>");

        foreach (var group in entries.GroupBy(e => e.IsUpdate ? locale.UpdatesHeading : locale.CompetitionName(e.Match.Competition)))
        {
            sb.Append($"<section><h2>{E(group.Key)}</h2>");
            foreach (var (match, isUpdate) in group)
                sb.Append(MatchRow(kind, locale, match, isUpdate, showDate: dates.Count > 1));
            sb.Append("</section>");
        }

        var pageLabel = pageCount > 1 ? $"{page}/{pageCount}" : "";
        sb.Append($"<footer><span>{E(theme.PageName)}</span><span>{pageLabel}</span></footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string DateLine(CardLocale locale, List<DateOnly> dates)
    {
        if (dates.Count == 0)
            return "";
        return dates.Count == 1
            ? $"{locale.Weekday(dates[0])}, {locale.YearDate(dates[0])}"
            : $"{locale.ShortDate(dates[0])} – {locale.YearDate(dates[^1])}";
    }

    private string MatchRow(DigestKind kind, CardLocale locale, Match m, bool isUpdate, bool showDate)
    {
        var center = isUpdate ? UpdateCenter(locale, m)
            : kind == DigestKind.Upcoming ? TimeCenter(locale, m, showDate)
            : ScoreCenter(locale, m, showDate);

        var below = isUpdate || kind == DigestKind.Upcoming
            ? $"<div class=\"below\"><div class=\"left\">{Form(m.Home.Form)}</div><div class=\"right\">{Form(m.Away.Form)}</div></div>"
            : $"<div class=\"below\"><div class=\"left\">{Events(locale, m, m.Home.Team.Id)}</div><div class=\"right\">{Events(locale, m, m.Away.Team.Id)}</div></div>";

        return $"""
            <article>
              <div class="line">
                <div class="team home"><span class="name">{E(clubs.DisplayName(m.Home.Team))}</span>{Logo(m.Home.Team)}</div>
                {center}
                <div class="team away">{Logo(m.Away.Team)}<span class="name">{E(clubs.DisplayName(m.Away.Team))}</span></div>
              </div>
              {below}
            </article>
            """;
    }

    private static string TimeCenter(CardLocale locale, Match m, bool showDate) =>
        $"<div class=\"center\"><div class=\"time\">{Clock(locale, m)}</div>{MatchDate(locale, m, showDate)}</div>";

    private static string ScoreCenter(CardLocale locale, Match m, bool showDate)
    {
        var shootout = m.Home.ShootoutScore is { } h && m.Away.ShootoutScore is { } a
            ? $"<div class=\"pen\">pen {h}–{a}</div>"
            : "";
        return $"<div class=\"center\"><div class=\"score\">{m.Home.Score}–{m.Away.Score}</div>{shootout}{MatchDate(locale, m, showDate)}</div>";
    }

    private static string MatchDate(CardLocale locale, Match m, bool showDate) => showDate
        ? $"<div class=\"pen\">{E(locale.ShortDate(LocalDate(locale, m)))}</div>"
        : "";

    private static string UpdateCenter(CardLocale locale, Match m) => m.Status switch
    {
        MatchStatus.Postponed => $"<div class=\"tag\">{E(locale.Postponed)}</div>",
        MatchStatus.Cancelled => $"<div class=\"tag\">{E(locale.Cancelled)}</div>",
        _ => $"<div class=\"center\"><div class=\"time\">{Clock(locale, m)}</div><div class=\"pen\">{E(locale.ShortDate(LocalDate(locale, m)))} · {E(locale.NewTime)}</div></div>",
    };

    private static string Clock(CardLocale locale, Match m) =>
        locale.ToLocalTime(m.KickoffUtc).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DateOnly LocalDate(CardLocale locale, Match m) =>
        DateOnly.FromDateTime(locale.ToLocalTime(m.KickoffUtc).DateTime);

    private static string Logo(Team team) => string.IsNullOrEmpty(team.LogoUrl)
        ? "<span class=\"logo\"></span>"
        : $"<span class=\"logo\"><img src=\"{E(team.LogoUrl)}\" alt=\"\" onerror=\"this.remove()\"></span>";

    private static string Events(CardLocale locale, Match m, string teamId)
    {
        var lines = m.Events
            .Where(e => e.TeamId == teamId && (e.IsGoal || e.Type == MatchEventType.RedCard))
            .Select(e => e.Type switch
            {
                MatchEventType.RedCard => $"<div><i class=\"red\"></i>{E(e.Player)} {E(e.Minute)}</div>",
                MatchEventType.PenaltyGoal => $"<div>{E(e.Player)} {E(e.Minute)} (pen)</div>",
                MatchEventType.OwnGoal => $"<div>{E(e.Player)} {E(e.Minute)} ({E(locale.OwnGoal)})</div>",
                _ => $"<div>{E(e.Player)} {E(e.Minute)}</div>",
            });
        return string.Concat(lines);
    }

    private static string Form(string? form)
    {
        if (string.IsNullOrEmpty(form))
            return "";
        var chips = form.Select(c => c switch
        {
            'W' => "<b class=\"w\">W</b>",
            'D' => "<b class=\"d\">D</b>",
            'L' => "<b class=\"l\">L</b>",
            _ => "",
        });
        return $"<div class=\"form\">{string.Concat(chips)}</div>";
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private string Css() => $$"""
        @font-face { font-family: BVP; font-weight: 700; src: url(data:font/ttf;base64,{{boldFontBase64}}); }
        @font-face { font-family: BVP; font-weight: 500; src: url(data:font/ttf;base64,{{mediumFontBase64}}); }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { width: 1080px; font-family: BVP, sans-serif; font-weight: 500; color: {{theme.Text}}; background: {{theme.Background}}; }
        .card { width: 1080px; padding: 56px 48px 40px; background: {{theme.Background}}; }
        header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 36px; }
        .heading { border-left: 12px solid {{theme.Accent}}; padding-left: 24px; }
        .page-logo { height: 170px; }
        h1 { font-size: 64px; font-weight: 700; line-height: 1.15; }
        .date { font-size: 34px; font-weight: 700; color: {{theme.Accent}}; margin-top: 4px; }
        header p { font-size: 28px; color: {{theme.Muted}}; margin-top: 2px; }
        section { margin-bottom: 28px; }
        h2 { font-size: 26px; font-weight: 700; letter-spacing: 2px; color: {{theme.Accent}}; margin-bottom: 14px; }
        article { background: {{theme.Surface}}; border-radius: 20px; padding: 22px 24px; margin-bottom: 14px; }
        .line { display: grid; grid-template-columns: 1fr 170px 1fr; align-items: center; column-gap: 14px; }
        .team { display: flex; align-items: center; gap: 16px; min-width: 0; }
        .home { justify-content: flex-end; text-align: right; }
        .name { font-size: 32px; font-weight: 700; line-height: 1.2; }
        .logo { flex: none; width: 72px; height: 72px; border-radius: 50%; background: #fff; display: flex; align-items: center; justify-content: center; }
        .logo img { width: 56px; height: 56px; object-fit: contain; }
        .center { text-align: center; }
        .score { font-size: 54px; font-weight: 700; color: {{theme.Accent}}; text-align: center; line-height: 1.1; }
        .time { font-size: 44px; font-weight: 700; color: {{theme.Accent}}; text-align: center; line-height: 1.15; }
        .pen { font-size: 20px; color: {{theme.Muted}}; text-align: center; }
        .tag { font-size: 24px; font-weight: 700; color: #1A1206; background: #FFB020; border-radius: 12px; padding: 10px 0; text-align: center; }
        .below { display: grid; grid-template-columns: 1fr 1fr; column-gap: 198px; margin-top: 10px; font-size: 23px; line-height: 1.4; color: {{theme.Muted}}; }
        .below:not(:has(div > *)) { display: none; }
        .left { text-align: right; }
        .form { display: inline-flex; gap: 6px; }
        .form b { width: 30px; height: 30px; border-radius: 6px; display: inline-flex; align-items: center; justify-content: center; font-size: 17px; font-weight: 700; color: #fff; }
        .form .w { background: #1E9E5A; } .form .d { background: #75807B; } .form .l { background: #D2433D; }
        .red { display: inline-block; width: 14px; height: 20px; border-radius: 3px; background: #E5383B; margin-right: 8px; vertical-align: -3px; }
        footer { display: flex; justify-content: space-between; font-size: 26px; font-weight: 700; color: {{theme.Muted}}; margin-top: 8px; }
        """;
}

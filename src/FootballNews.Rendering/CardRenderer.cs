using FootballNews.Core.Models;
using Microsoft.Playwright;

namespace FootballNews.Rendering;

public sealed record CardImage(string Language, int Page, byte[] Png);

/// <summary>Ảnh của một giải (hoặc các giải ghép chung), mỗi trang đủ các bản ngôn ngữ.</summary>
public sealed record CardSection(IReadOnlyList<Match> Updates, IReadOnlyList<Match> Matches, int PageCount, IReadOnlyList<CardImage> Images);

/// <summary>Chụp HTML thành PNG bằng Chrome hoặc Edge có sẵn trên máy.</summary>
public sealed class CardRenderer(CardHtml html)
{
    public async Task<IReadOnlyList<CardSection>> RenderAsync(Digest digest)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(playwright);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1080, Height = 1350 },
        });

        var byLocale = CardLocale.All.Select(locale => (locale.Code, Sections: html.BuildSections(digest, locale))).ToList();
        var pageNumber = 0;
        var result = new List<CardSection>();
        for (var s = 0; s < byLocale[0].Sections.Count; s++)
        {
            var images = new List<CardImage>();
            foreach (var (code, sections) in byLocale)
            {
                var number = pageNumber;
                foreach (var content in sections[s].Pages)
                {
                    await page.SetContentAsync(content, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
                    await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
                    var png = await page.Locator(".card").ScreenshotAsync(new LocatorScreenshotOptions { Type = ScreenshotType.Png });
                    images.Add(new CardImage(code, ++number, png));
                }
            }

            var section = byLocale[0].Sections[s];
            pageNumber += section.Pages.Count;
            result.Add(new CardSection(section.Updates, section.Matches, section.Pages.Count, images));
        }
        return result;
    }
}

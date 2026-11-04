using FootballNews.Core.Models;
using Microsoft.Playwright;

namespace FootballNews.Rendering;

public sealed record CardImage(string Language, int Page, byte[] Png);

/// <summary>Chụp HTML thành PNG bằng Chrome hoặc Edge có sẵn trên máy.</summary>
public sealed class CardRenderer(CardHtml html)
{
    private static readonly string[] BrowserChannels = ["chrome", "msedge"];

    public async Task<IReadOnlyList<CardImage>> RenderAsync(Digest digest)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1080, Height = 1350 },
        });

        var images = new List<CardImage>();
        foreach (var locale in CardLocale.All)
        {
            var contents = html.BuildPages(digest, locale);
            for (var i = 0; i < contents.Count; i++)
            {
                await page.SetContentAsync(contents[i], new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
                var png = await page.Locator(".card").ScreenshotAsync(new LocatorScreenshotOptions { Type = ScreenshotType.Png });
                images.Add(new CardImage(locale.Code, i + 1, png));
            }
        }
        return images;
    }

    private static async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        foreach (var channel in BrowserChannels)
        {
            try
            {
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = channel });
            }
            catch (PlaywrightException)
            {
                Console.WriteLine($"Không mở được trình duyệt {channel}, thử trình duyệt khác.");
            }
        }
        throw new InvalidOperationException("Không tìm thấy Chrome hoặc Edge để vẽ ảnh.");
    }
}

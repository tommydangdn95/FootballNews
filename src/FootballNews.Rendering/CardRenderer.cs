using FootballNews.Core.Models;
using Microsoft.Playwright;

namespace FootballNews.Rendering;

/// <summary>Chụp HTML thành PNG bằng Chrome hoặc Edge có sẵn trên máy.</summary>
public sealed class CardRenderer(CardHtml html)
{
    private static readonly string[] BrowserChannels = ["chrome", "msedge"];

    public async Task<IReadOnlyList<byte[]>> RenderAsync(Digest digest)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1080, Height = 1350 },
        });

        var images = new List<byte[]>();
        foreach (var content in html.BuildPages(digest))
        {
            await page.SetContentAsync(content, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
            images.Add(await page.Locator(".card").ScreenshotAsync(new LocatorScreenshotOptions { Type = ScreenshotType.Png }));
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

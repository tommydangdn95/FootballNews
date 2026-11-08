using Microsoft.Playwright;

namespace FootballNews.Rendering;

/// <summary>Chụp ảnh live thành PNG. Mỗi lần vẽ mở trình duyệt riêng vì cả trận chỉ vẽ vài chục ảnh.</summary>
public sealed class LiveCardRenderer(LiveCardHtml html)
{
    public async Task<byte[]> RenderAsync(LiveCard card)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(playwright);
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1080, Height = 1080 },
        });
        await page.SetContentAsync(html.Build(card), new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
        await page.EvaluateAsync(LiveCardHtml.FitScript);
        return await page.Locator(".card").ScreenshotAsync(new LocatorScreenshotOptions { Type = ScreenshotType.Png });
    }
}

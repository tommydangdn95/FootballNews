using Microsoft.Playwright;

namespace FootballNews.Rendering;

/// <summary>Mở Chrome hoặc Edge có sẵn trên máy, không tải trình duyệt riêng của Playwright.</summary>
internal static class BrowserLauncher
{
    private static readonly string[] BrowserChannels = ["chrome", "msedge"];

    public static async Task<IBrowser> LaunchAsync(IPlaywright playwright)
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

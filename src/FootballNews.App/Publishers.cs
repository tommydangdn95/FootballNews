using FootballNews.Core.Formatting;
using FootballNews.Core.Models;
using FootballNews.Telegram;

namespace FootballNews.App;

internal interface IPublisher
{
    Task PublishAsync(DigestKind kind, IReadOnlyList<byte[]> cards, IReadOnlyList<Draft> drafts, CancellationToken ct);
    Task NotifyAsync(string message, CancellationToken ct);
}

internal sealed class TelegramPublisher(TelegramClient telegram) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, IReadOnlyList<byte[]> cards, IReadOnlyList<Draft> drafts, CancellationToken ct)
    {
        for (var i = 0; i < cards.Count; i++)
            await telegram.SendPhotoAsync(cards[i], $"{kind.ToString().ToLowerInvariant()}-{i + 1}.png", ct);

        foreach (var message in drafts.SelectMany(TelegramMessages.Build))
            await telegram.SendHtmlAsync(message, ct);
    }

    public Task NotifyAsync(string message, CancellationToken ct) => telegram.SendPlainAsync(message, ct);
}

/// <summary>Chạy thử: ghi ảnh và bài nháp ra thư mục, không gửi đi đâu.</summary>
internal sealed class FilePublisher(string directory) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, IReadOnlyList<byte[]> cards, IReadOnlyList<Draft> drafts, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var prefix = kind.ToString().ToLowerInvariant();
        for (var i = 0; i < cards.Count; i++)
            await File.WriteAllBytesAsync(Path.Combine(directory, $"{prefix}-card-{i + 1}.png"), cards[i], ct);

        foreach (var draft in drafts)
        {
            var text = string.Join("\n\n--- tweet tiếp theo ---\n\n", draft.Parts);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{prefix}-{draft.Platform.ToLowerInvariant()}.txt"), text, ct);
        }
        Console.WriteLine($"Đã ghi {prefix} vào {directory}");
    }

    public Task NotifyAsync(string message, CancellationToken ct)
    {
        Console.WriteLine($"[Thông báo] {message}");
        return Task.CompletedTask;
    }
}

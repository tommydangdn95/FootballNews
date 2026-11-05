using FootballNews.Core.Formatting;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Telegram;

namespace FootballNews.App;

internal interface IPublisher
{
    Task PublishAsync(DigestKind kind, IReadOnlyList<CardImage> cards, IReadOnlyList<Draft> drafts, CancellationToken ct);
    Task NotifyAsync(string message, CancellationToken ct);
}

internal static class CardFileName
{
    public static string For(DigestKind kind, CardImage card) =>
        $"{kind.ToString().ToLowerInvariant()}-{card.Language}-{card.Page}.png";
}

internal sealed class TelegramPublisher(TelegramClient telegram) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, IReadOnlyList<CardImage> cards, IReadOnlyList<Draft> drafts, CancellationToken ct)
    {
        foreach (var card in cards)
            await telegram.SendPhotoAsync(card.Png, CardFileName.For(kind, card), ct);

        foreach (var message in drafts.SelectMany(TelegramMessages.Build))
            await telegram.SendHtmlAsync(message, ct);
    }

    public Task NotifyAsync(string message, CancellationToken ct) => telegram.SendPlainAsync(message, ct);
}

/// <summary>Chạy thử: ghi ảnh và bài nháp ra thư mục, không gửi đi đâu.</summary>
internal sealed class FilePublisher(string directory) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, IReadOnlyList<CardImage> cards, IReadOnlyList<Draft> drafts, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        foreach (var card in cards)
            await File.WriteAllBytesAsync(Path.Combine(directory, CardFileName.For(kind, card)), card.Png, ct);

        var prefix = kind.ToString().ToLowerInvariant();
        foreach (var draft in drafts)
        {
            var text = string.Join("\n\n--- bài tiếp theo ---\n\n", draft.Parts);
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

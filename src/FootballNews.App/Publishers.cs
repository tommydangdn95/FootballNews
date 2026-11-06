using System.Text;
using FootballNews.Core.Formatting;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Telegram;

namespace FootballNews.App;

internal interface IPublisher
{
    Task PublishAsync(DigestKind kind, string header, IReadOnlyList<CardSection> sections, CancellationToken ct);
    Task NotifyAsync(string message, CancellationToken ct);
}

internal static class CardFileName
{
    public static string For(DigestKind kind, CardImage card) =>
        $"{kind.ToString().ToLowerInvariant()}-{card.Language}-{card.Page}.png";
}

internal sealed class TelegramPublisher(TelegramClient telegram) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, string header, IReadOnlyList<CardSection> sections, CancellationToken ct)
    {
        await telegram.SendPlainAsync(header, ct);

        foreach (var section in sections)
        {
            await telegram.SendPlainAsync(DigestText.Section(section.Updates, section.Matches, section.PageCount), ct);
            foreach (var card in section.Images)
                await telegram.SendPhotoAsync(card.Png, CardFileName.For(kind, card), ct);
        }
    }

    public Task NotifyAsync(string message, CancellationToken ct) => telegram.SendPlainAsync(message, ct);
}

/// <summary>Chạy thử: ghi ảnh và thứ tự tin nhắn ra thư mục, không gửi đi đâu.</summary>
internal sealed class FilePublisher(string directory) : IPublisher
{
    public async Task PublishAsync(DigestKind kind, string header, IReadOnlyList<CardSection> sections, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var prefix = kind.ToString().ToLowerInvariant();
        var order = new StringBuilder(header).Append("\n\n");

        foreach (var section in sections)
        {
            order.Append(DigestText.Section(section.Updates, section.Matches, section.PageCount)).Append('\n');
            foreach (var card in section.Images)
            {
                var name = CardFileName.For(kind, card);
                await File.WriteAllBytesAsync(Path.Combine(directory, name), card.Png, ct);
                order.Append("[ảnh] ").Append(name).Append('\n');
            }
            order.Append('\n');
        }

        await File.WriteAllTextAsync(Path.Combine(directory, $"{prefix}-messages.txt"), order.ToString(), ct);
        Console.WriteLine($"Đã ghi {prefix} vào {directory}");
    }

    public Task NotifyAsync(string message, CancellationToken ct)
    {
        Console.WriteLine($"[Thông báo] {message}");
        return Task.CompletedTask;
    }
}

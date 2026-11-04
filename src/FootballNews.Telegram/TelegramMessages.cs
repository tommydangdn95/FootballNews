using System.Net;
using System.Text;
using FootballNews.Core.Formatting;

namespace FootballNews.Telegram;

/// <summary>Dựng tin nhắn HTML cho Telegram. Nội dung nằm trong thẻ pre để bấm là sao chép được.</summary>
public static class TelegramMessages
{
    // Telegram cho tối đa 4096 ký tự; chừa chỗ cho dòng nhãn.
    public const int MaxChunkLength = 3900;

    public static IEnumerable<string> Build(Draft draft)
    {
        var chunks = draft.Parts.SelectMany(part => Split(part, MaxChunkLength)).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            var label = chunks.Count > 1 ? $"{draft.Platform} ({i + 1}/{chunks.Count})" : draft.Platform;
            yield return $"<b>{WebUtility.HtmlEncode(label)}</b>\n<pre>{WebUtility.HtmlEncode(chunks[i])}</pre>";
        }
    }

    /// <summary>Chia theo dòng; chỉ cắt giữa dòng khi một dòng dài hơn giới hạn.</summary>
    public static IEnumerable<string> Split(string text, int maxLength)
    {
        var current = new StringBuilder();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            while (line.Length > maxLength)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                var cut = char.IsHighSurrogate(line[maxLength - 1]) ? maxLength - 1 : maxLength;
                yield return line[..cut];
                line = line[cut..];
            }

            if (current.Length > 0 && current.Length + 1 + line.Length > maxLength)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0)
                current.Append('\n');
            current.Append(line);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }
}

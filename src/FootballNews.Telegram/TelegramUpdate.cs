using System.Text.Json;

namespace FootballNews.Telegram;

/// <param name="ReplyToMessageId">Tin này trả lời (reply) tin nào, nếu có.</param>
public sealed record TelegramMessage(long MessageId, long ChatId, long FromId, string? Text, long? ReplyToMessageId);

/// <summary>Một lần bấm nút dưới tin nhắn.</summary>
public sealed record TelegramCallback(string Id, long FromId, long ChatId, long MessageId, string Data);

public sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message, TelegramCallback? Callback)
{
    public static IReadOnlyList<TelegramUpdate> ParseList(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array)
            return [];
        return result.EnumerateArray().Select(Parse).ToList();
    }

    public static TelegramUpdate Parse(JsonElement u)
    {
        var id = u.GetProperty("update_id").GetInt64();
        TelegramMessage? message = null;
        TelegramCallback? callback = null;

        if (u.TryGetProperty("message", out var m))
            message = ParseMessage(m);

        if (u.TryGetProperty("callback_query", out var c))
        {
            var cm = c.TryGetProperty("message", out var msg) ? msg : default;
            callback = new TelegramCallback(
                c.GetProperty("id").GetString() ?? "",
                From(c),
                cm.ValueKind == JsonValueKind.Object ? cm.GetProperty("chat").GetProperty("id").GetInt64() : 0,
                cm.ValueKind == JsonValueKind.Object ? cm.GetProperty("message_id").GetInt64() : 0,
                c.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "");
        }
        return new TelegramUpdate(id, message, callback);
    }

    private static TelegramMessage ParseMessage(JsonElement m) => new(
        m.GetProperty("message_id").GetInt64(),
        m.GetProperty("chat").GetProperty("id").GetInt64(),
        From(m),
        m.TryGetProperty("text", out var t) ? t.GetString() : m.TryGetProperty("caption", out var cap) ? cap.GetString() : null,
        m.TryGetProperty("reply_to_message", out var r) ? r.GetProperty("message_id").GetInt64() : null);

    private static long From(JsonElement e) =>
        e.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var id) ? id.GetInt64() : 0;
}

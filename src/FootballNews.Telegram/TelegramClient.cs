using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FootballNews.Telegram;

public sealed class TelegramException(string message) : Exception(message);

/// <summary>Một nút bấm dưới tin nhắn. <paramref name="Data"/> tối đa 64 byte.</summary>
public sealed record InlineButton(string Text, string Data);

public sealed class TelegramClient(HttpClient http, string token, string chatId)
{
    private const int MaxAttempts = 5;

    // Giới hạn của Telegram.
    public const int MaxCaption = 1024;
    public const int MaxText = 4096;

    public Task SendPlainAsync(string text, CancellationToken ct) => SendMessageAsync(text, null, ct);

    public Task SendPhotoAsync(byte[] png, string fileName, CancellationToken ct) => SendPhotoAsync(png, fileName, null, null, ct);

    /// <summary>Gửi tin chữ, trả id tin. <paramref name="toChatId"/> để trả lời chat khác chat mặc định.</summary>
    public async Task<long> SendMessageAsync(string text, IReadOnlyList<IReadOnlyList<InlineButton>>? buttons, CancellationToken ct, string? toChatId = null)
    {
        var form = new Dictionary<string, string>
        {
            ["chat_id"] = toChatId ?? chatId,
            ["text"] = Truncate(text, MaxText),
            ["disable_web_page_preview"] = "true",
        };
        if (buttons is not null)
            form["reply_markup"] = Keyboard(buttons);
        var result = await CallAsync("sendMessage", () => new FormUrlEncodedContent(form), ct);
        return MessageId(result);
    }

    public async Task<long> SendPhotoAsync(byte[] png, string fileName, string? caption, IReadOnlyList<IReadOnlyList<InlineButton>>? buttons, CancellationToken ct)
    {
        var result = await CallAsync("sendPhoto", () =>
        {
            var photo = new ByteArrayContent(png);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var content = new MultipartFormDataContent
            {
                { new StringContent(chatId), "chat_id" },
                { photo, "photo", fileName },
            };
            if (caption is not null)
                content.Add(new StringContent(Truncate(caption, MaxCaption)), "caption");
            if (buttons is not null)
                content.Add(new StringContent(Keyboard(buttons)), "reply_markup");
            return content;
        }, ct);
        return MessageId(result);
    }

    /// <summary>Sửa chữ của tin (caption nếu là ảnh). Không truyền nút thì các nút cũ bị gỡ.</summary>
    public async Task EditAsync(long messageId, bool isPhoto, string text, IReadOnlyList<IReadOnlyList<InlineButton>>? buttons, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["message_id"] = messageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [isPhoto ? "caption" : "text"] = Truncate(text, isPhoto ? MaxCaption : MaxText),
        };
        if (!isPhoto)
            form["disable_web_page_preview"] = "true";
        if (buttons is not null)
            form["reply_markup"] = Keyboard(buttons);
        try
        {
            await CallAsync(isPhoto ? "editMessageCaption" : "editMessageText", () => new FormUrlEncodedContent(form), ct);
        }
        catch (TelegramException ex) when (ex.Message.Contains("message is not modified"))
        {
        }
    }

    /// <summary>Tắt vòng xoay trên nút vừa bấm, có thể kèm một dòng thông báo nhỏ.</summary>
    public async Task AnswerCallbackAsync(string callbackId, string? text, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
        if (text is not null)
            form["text"] = text;
        try
        {
            await CallAsync("answerCallbackQuery", () => new FormUrlEncodedContent(form), ct);
        }
        catch (TelegramException ex)
        {
            // Nút bấm quá lâu thì Telegram không nhận trả lời nữa; không ảnh hưởng việc xử lý.
            Console.WriteLine($"Bỏ qua lỗi answerCallbackQuery: {ex.Message}");
        }
    }

    /// <summary>
    /// Đọc tin nhắn và nút bấm mới. Gọi với <paramref name="offset"/> = id cuối + 1 để đánh dấu đã đọc.
    /// <paramref name="timeoutSeconds"/> lớn hơn 0 thì chờ đến khi có tin (long polling).
    /// </summary>
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, int timeoutSeconds, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["timeout"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["allowed_updates"] = "[\"message\",\"callback_query\"]",
        };
        if (offset is { } o)
            form["offset"] = o.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = await CallAsync("getUpdates", () => new FormUrlEncodedContent(form), ct);
        return TelegramUpdate.ParseList(result);
    }

    private async Task<JsonElement> CallAsync(string method, Func<HttpContent> content, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var body = content();
            using var response = await PostAsync(method, body, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }
                throw new TelegramException($"Telegram {method} lỗi {(int)response.StatusCode}: phản hồi không phải JSON");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (response.IsSuccessStatusCode)
                    return root.TryGetProperty("result", out var result) ? result.Clone() : default;

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    var wait = root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var r) ? r.GetInt32() : 5;
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                    continue;
                }
                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }
                var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
                throw new TelegramException($"Telegram {method} lỗi {(int)response.StatusCode}: {description}");
            }
        }
    }

    // URL của Telegram chứa token, nên không để lỗi mạng mang URL đi vào log hay tin báo lỗi.
    private async Task<HttpResponseMessage> PostAsync(string method, HttpContent body, CancellationToken ct)
    {
        try
        {
            return await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", body, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TelegramException($"Telegram {method}: lỗi mạng ({ex.HttpRequestError})");
        }
    }

    private static long MessageId(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message_id", out var id) ? id.GetInt64() : 0;

    private static string Keyboard(IReadOnlyList<IReadOnlyList<InlineButton>> rows) => JsonSerializer.Serialize(new
    {
        inline_keyboard = rows.Select(row => row.Select(b => new { text = b.Text, callback_data = b.Data })),
    });

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

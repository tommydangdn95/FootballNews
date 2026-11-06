using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FootballNews.Telegram;

public sealed class TelegramException(string message) : Exception(message);

public sealed class TelegramClient(HttpClient http, string token, string chatId)
{
    private const int MaxAttempts = 5;

    public Task SendPlainAsync(string text, CancellationToken ct) => SendAsync("sendMessage", () => new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["chat_id"] = chatId,
        ["text"] = text,
    }), ct);

    public Task SendPhotoAsync(byte[] png, string fileName, CancellationToken ct) => SendAsync("sendPhoto", () =>
    {
        var photo = new ByteArrayContent(png);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent
        {
            { new StringContent(chatId), "chat_id" },
            { photo, "photo", fileName },
        };
    }, ct);

    private async Task SendAsync(string method, Func<HttpContent> content, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var body = content();
            using var response = await PostAsync(method, body, ct);
            if (response.IsSuccessStatusCode)
                return;

            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = error.RootElement;
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                var wait = root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var r) ? r.GetInt32() : 5;
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                continue;
            }
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            throw new TelegramException($"Telegram {method} lỗi {(int)response.StatusCode}: {description}");
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
}

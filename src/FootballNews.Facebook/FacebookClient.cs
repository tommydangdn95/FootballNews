using System.Net.Http.Headers;
using System.Text.Json;

namespace FootballNews.Facebook;

public sealed class FacebookException(string message) : Exception(message);

/// <summary>Đăng bài lên Fanpage qua Graph API bằng Page access token.</summary>
public sealed class FacebookClient(HttpClient http, string pageId, string pageToken)
{
    public const string ApiVersion = "v27.0";
    private const string BaseUrl = $"https://graph.facebook.com/{ApiVersion}";

    /// <summary>Đăng một ảnh kèm caption thành bài mới. Trả id bài viết (dạng pageId_postId).</summary>
    public async Task<string> PostPhotoAsync(byte[] png, string message, CancellationToken ct)
    {
        var photo = new ByteArrayContent(png);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var content = new MultipartFormDataContent
        {
            { photo, "source", "live.png" },
            { new StringContent(message), "message" },
            { new StringContent("true"), "published" },
            // Token để trong body, không để trong URL, để lỗi mạng không làm lộ token vào log.
            { new StringContent(pageToken), "access_token" },
        };
        using var result = await PostAsync($"{BaseUrl}/{pageId}/photos", content, ct);
        var root = result.RootElement;
        return root.TryGetProperty("post_id", out var postId) ? postId.GetString()!
            : root.TryGetProperty("id", out var id) ? id.GetString()!
            : throw new FacebookException("Facebook không trả id bài viết.");
    }

    /// <summary>Đăng bài chữ. Dùng cho /fbtest và khi không vẽ được ảnh.</summary>
    public async Task<string> PostTextAsync(string message, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = message,
            ["access_token"] = pageToken,
        });
        using var result = await PostAsync($"{BaseUrl}/{pageId}/feed", content, ct);
        return result.RootElement.TryGetProperty("id", out var id) ? id.GetString()!
            : throw new FacebookException("Facebook không trả id bài viết.");
    }

    public static string PostUrl(string postId) => $"https://www.facebook.com/{postId}";

    private async Task<JsonDocument> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, content, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new FacebookException($"Lỗi mạng khi gọi Facebook ({ex.HttpRequestError}).");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                throw new FacebookException($"Facebook lỗi {(int)response.StatusCode}: phản hồi không phải JSON.");
            }
            if (response.IsSuccessStatusCode)
                return doc;

            using (doc)
            {
                var error = doc.RootElement.TryGetProperty("error", out var e) ? e : default;
                var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() : null;
                var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c) ? c.ToString() : "?";
                throw new FacebookException($"Facebook lỗi {(int)response.StatusCode} (mã {code}): {message}{Hint(code)}");
            }
        }
    }

    // Mã 190: token hết hiệu lực. Mã 10/200: thiếu quyền.
    private static string Hint(string code) => code switch
    {
        "190" => " → Page token đã hết hiệu lực, lấy lại token theo hướng dẫn rồi cập nhật secret FB_PAGE_TOKEN.",
        "10" or "200" => " → App thiếu quyền pages_manage_posts hoặc bạn không còn quyền quản trị Page.",
        _ => "",
    };
}

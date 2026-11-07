using System.Text.Json;
using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Live;

/// <param name="Hashtag">Hashtag của trang, không có dấu #.</param>
/// <param name="PollSeconds">Bao lâu hỏi ESPN một lần trong lúc trận đang đá.</param>
/// <param name="DraftMinutes">Bản nháp hết hạn sau bao nhiêu phút, tính từ lúc bot phát hiện sự kiện.</param>
/// <param name="Gradients">Màu nền ảnh theo loại sự kiện (khoá là tên <see cref="LiveEventKind"/> dạng camelCase), mỗi loại 2 màu.</param>
/// <param name="ExtraCompetitions">Các cúp ngoài 8 giải ở mục 3, chỉ dùng để tìm trận cho lệnh /watch.</param>
/// <param name="WatchLimitMinutes">Quá chừng này phút sau giờ bóng lăn mà ESPN chưa báo kết thúc thì bỏ theo dõi.</param>
/// <param name="WatchLimitExtraTimeMinutes">Như trên, khi trận đã vào hiệp phụ.</param>
public sealed record LiveConfig(
    string Hashtag,
    int PollSeconds,
    int DraftMinutes,
    Dictionary<string, string[]> Gradients,
    List<Competition> ExtraCompetitions,
    int WatchLimitMinutes = 180,
    int WatchLimitExtraTimeMinutes = 225)
{
    public static LiveConfig Load(string path) =>
        JsonSerializer.Deserialize<LiveConfig>(File.ReadAllText(path), JsonDefaults.Options)
        ?? throw new InvalidDataException($"Không đọc được {path}");

    /// <summary>Mốc an toàn phòng khi ESPN bị kẹt, không bao giờ báo trận kết thúc.</summary>
    public DateTimeOffset WatchDeadline(WatchedMatch watch) =>
        watch.KickoffUtc.AddMinutes(watch.HadExtraTime ? WatchLimitExtraTimeMinutes : WatchLimitMinutes);

    public (string From, string To) Gradient(LiveEventKind kind)
    {
        var key = JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
        return Gradients.TryGetValue(key, out var colors) && colors.Length >= 2
            ? (colors[0], colors[1])
            : ("#FF0F5B", "#3B2BD6");
    }
}

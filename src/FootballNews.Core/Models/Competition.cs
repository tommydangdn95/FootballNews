namespace FootballNews.Core.Models;

/// <param name="Logo">Logo giải (bản cho nền tối của ESPN), vẽ trên ảnh bảng xếp hạng. Không có thì chỉ ghi tên giải.</param>
/// <param name="Rounds">Tổng số lượt trận của giải VĐQG hoặc vòng phân hạng cúp châu Âu, ghi trên ảnh dạng "4/8". Không có thì chỉ ghi số lượt.</param>
public sealed record Competition(string Slug, string Name, string EnglishName, string Emoji, string Hashtag, int Order, IReadOnlyList<DayOfWeek> UpcomingWeekdays, int? Rounds = null, string? Logo = null)
{
    /// <summary>Job daily có gửi lịch đấu của giải này vào ngày đó không.</summary>
    public bool IsUpcomingOn(DateOnly vietnamDate) => UpcomingWeekdays.Contains(vietnamDate.DayOfWeek);
}

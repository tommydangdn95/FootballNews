namespace FootballNews.Core.Models;

/// <param name="Rounds">Tổng số lượt trận của giải VĐQG hoặc vòng phân hạng cúp châu Âu, ghi trên ảnh dạng "4/8". Không có thì chỉ ghi số lượt.</param>
public sealed record Competition(string Slug, string Name, string EnglishName, string Emoji, string Hashtag, int Order, IReadOnlyList<DayOfWeek> UpcomingWeekdays, int? Rounds = null)
{
    /// <summary>Job daily có gửi lịch đấu của giải này vào ngày đó không.</summary>
    public bool IsUpcomingOn(DateOnly vietnamDate) => UpcomingWeekdays.Contains(vietnamDate.DayOfWeek);
}

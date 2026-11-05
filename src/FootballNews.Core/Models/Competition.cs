namespace FootballNews.Core.Models;

public sealed record Competition(string Slug, string Name, string EnglishName, string Emoji, string Hashtag, int Order, IReadOnlyList<DayOfWeek> UpcomingWeekdays)
{
    /// <summary>Job daily có gửi lịch đấu của giải này vào ngày đó không.</summary>
    public bool IsUpcomingOn(DateOnly vietnamDate) => UpcomingWeekdays.Contains(vietnamDate.DayOfWeek);
}

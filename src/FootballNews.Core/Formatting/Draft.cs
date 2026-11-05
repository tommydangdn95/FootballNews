namespace FootballNews.Core.Formatting;

/// <summary>Bản nháp cho một nền tảng. X và Threads có thể gồm nhiều bài nối tiếp; Facebook chỉ có một phần.</summary>
public sealed record Draft(string Platform, IReadOnlyList<string> Parts);

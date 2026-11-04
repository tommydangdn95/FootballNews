namespace FootballNews.Core.Formatting;

/// <summary>Bản nháp cho một nền tảng. X có thể gồm nhiều tweet; các nền tảng khác chỉ có một phần.</summary>
public sealed record Draft(string Platform, IReadOnlyList<string> Parts);

namespace FootballNews.Core.Models;

public sealed record Competition(string Slug, string Name, string EnglishName, string Emoji, string Hashtag, int Order);

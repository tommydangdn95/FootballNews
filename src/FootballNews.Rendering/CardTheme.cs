using System.Text.Json;
using FootballNews.Core.Config;

namespace FootballNews.Rendering;

public sealed record CardTheme(string PageName, string Background, string Surface, string SurfaceEnd, string SurfaceText, string SurfaceMuted, string Accent, string Text, string Muted)
{
    public static CardTheme Load(string path) =>
        JsonSerializer.Deserialize<CardTheme>(File.ReadAllText(path), JsonDefaults.Options)
        ?? throw new InvalidDataException($"Không đọc được {path}");
}

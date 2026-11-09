using System.Text.Json;
using FootballNews.Core.Config;

namespace FootballNews.Rendering;

/// <summary>Màu riêng của một giải. Màu nào để trống thì lấy màu mặc định của <see cref="CardTheme"/>.</summary>
public sealed record ThemeColors(
    string? Background = null,
    string? Surface = null,
    string? SurfaceEnd = null,
    string? SurfaceText = null,
    string? SurfaceMuted = null,
    string? Accent = null,
    string? Text = null,
    string? Muted = null);

/// <param name="Competitions">Màu riêng theo slug giải (vd <c>eng.1</c>) để khán giả nhận ra giải qua màu ảnh.</param>
public sealed record CardTheme(
    string PageName,
    string Background,
    string Surface,
    string SurfaceEnd,
    string SurfaceText,
    string SurfaceMuted,
    string Accent,
    string Text,
    string Muted,
    IReadOnlyDictionary<string, ThemeColors>? Competitions = null)
{
    public static CardTheme Load(string path) =>
        JsonSerializer.Deserialize<CardTheme>(File.ReadAllText(path), JsonDefaults.Options)
        ?? throw new InvalidDataException($"Không đọc được {path}");

    /// <summary>Màu dùng cho ảnh của giải <paramref name="slug"/>; giải không khai báo màu riêng thì dùng màu mặc định.</summary>
    public CardTheme For(string? slug) =>
        slug is not null && Competitions is not null && Competitions.TryGetValue(slug, out var c)
            ? this with
            {
                Background = c.Background ?? Background,
                Surface = c.Surface ?? Surface,
                SurfaceEnd = c.SurfaceEnd ?? SurfaceEnd,
                SurfaceText = c.SurfaceText ?? SurfaceText,
                SurfaceMuted = c.SurfaceMuted ?? SurfaceMuted,
                Accent = c.Accent ?? Accent,
                Text = c.Text ?? Text,
                Muted = c.Muted ?? Muted,
            }
            : this;
}

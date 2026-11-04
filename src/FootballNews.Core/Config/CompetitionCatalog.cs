using System.Text.Json;
using FootballNews.Core.Models;

namespace FootballNews.Core.Config;

public static class CompetitionCatalog
{
    public static IReadOnlyList<Competition> Load(string path)
    {
        var file = JsonSerializer.Deserialize<CompetitionsFile>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException($"Không đọc được {path}");
        return file.Competitions.OrderBy(c => c.Order).ToList();
    }

    private sealed record CompetitionsFile(List<Competition> Competitions);
}

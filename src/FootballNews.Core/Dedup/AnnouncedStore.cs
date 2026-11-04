using System.Text.Json;
using FootballNews.Core.Config;

namespace FootballNews.Core.Dedup;

public sealed class AnnouncedStore(string path)
{
    public AnnouncedState Load()
    {
        if (!File.Exists(path))
            return new AnnouncedState();
        return JsonSerializer.Deserialize<AnnouncedState>(File.ReadAllText(path), JsonDefaults.Options)
            ?? new AnnouncedState();
    }

    public void Save(AnnouncedState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonDefaults.Options) + "\n");
}

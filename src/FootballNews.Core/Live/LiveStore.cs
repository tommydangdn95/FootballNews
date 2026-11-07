using System.Text.Json;
using FootballNews.Core.Config;

namespace FootballNews.Core.Live;

public sealed class LiveStore(string path)
{
    public string Path => path;

    public LiveState Load()
    {
        if (!File.Exists(path))
            return new LiveState();
        return JsonSerializer.Deserialize<LiveState>(File.ReadAllText(path), JsonDefaults.Options) ?? new LiveState();
    }

    public void Save(LiveState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonDefaults.Options) + "\n");
}

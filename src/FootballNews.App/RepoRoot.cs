namespace FootballNews.App;

internal static class RepoRoot
{
    public static string Find()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config", "clubs.json")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc chứa config/clubs.json");
    }
}

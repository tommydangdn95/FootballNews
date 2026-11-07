using System.Diagnostics;

namespace FootballNews.App;

/// <summary>
/// Commit và push file trạng thái ngay trong lúc chạy, để job chết giữa chừng thì lần sau không đăng lại.
/// Chỉ chạy khi <c>LIVE_GIT_COMMIT=true</c> (đặt trong workflow); chạy trên máy thì chỉ ghi file.
/// </summary>
internal sealed class GitCommitter(string repoRoot, bool enabled)
{
    public async Task CommitAsync(string path, string message)
    {
        if (!enabled || !File.Exists(path))
            return;
        try
        {
            await RunAsync("add", "--", path);
            if (await RunAsync("diff", "--cached", "--quiet") == 0)
                return;
            await RunAsync("commit", "-m", message);
            await RunAsync("pull", "--rebase");
            if (await RunAsync("push") != 0)
                Console.WriteLine("git push lỗi, lần lưu sau sẽ thử lại.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Không commit được trạng thái: {ex.Message}");
        }
    }

    private async Task<int> RunAsync(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Không chạy được git.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 && args[0] != "diff")
            Console.WriteLine($"git {args[0]} lỗi {process.ExitCode}: {error.Trim()} {output.Trim()}");
        return process.ExitCode;
    }
}

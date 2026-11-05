using System.Globalization;
using System.Text;
using FootballNews.App;
using FootballNews.Core.Config;
using FootballNews.Core.Dedup;
using FootballNews.Core.Filtering;
using FootballNews.Core.Models;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;
using FootballNews.Telegram;

Console.OutputEncoding = Encoding.UTF8;

const string usage = "Cách dùng: <daily|results|upcoming> [--date yyyy-MM-dd] [--dry-run] [--force]";

DigestKind[]? kinds = args.FirstOrDefault() switch
{
    "daily" => [DigestKind.Results, DigestKind.Upcoming],
    "results" => [DigestKind.Results],
    "upcoming" => [DigestKind.Upcoming],
    _ => null,
};
if (kinds is null)
{
    Console.Error.WriteLine(usage);
    return 2;
}

var date = ReportWindow.VietnamToday(DateTimeOffset.UtcNow);
var dryRun = false;
var force = false;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--dry-run")
    {
        dryRun = true;
    }
    else if (args[i] == "--force")
    {
        force = true;
    }
    else if (args[i] == "--date" && i + 1 < args.Length
        && DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
    {
        date = parsed;
    }
    else
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
}

var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
if (!dryRun && (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)))
{
    Console.Error.WriteLine("Thiếu TELEGRAM_BOT_TOKEN hoặc TELEGRAM_CHAT_ID. Dùng --dry-run để chạy thử không gửi.");
    return 2;
}

var root = FindRepoRoot();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

IPublisher publisher = dryRun
    ? new FilePublisher(Path.Combine(root, "output", date.ToString("yyyy-MM-dd")))
    : new TelegramPublisher(new TelegramClient(http, token!, chatId!));

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
try
{
    var clubs = ClubCatalog.Load(Path.Combine(root, "config", "clubs.json"));
    var competitions = CompetitionCatalog.Load(Path.Combine(root, "config", "competitions.json"));
    var theme = CardTheme.Load(Path.Combine(root, "config", "card.json"));
    var pageLogo = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "assets", "logo.png")));
    var renderer = new CardRenderer(new CardHtml(clubs, theme, LoadFont("Bold"), LoadFont("Medium"), pageLogo));

    // Job daily gửi lịch theo nhóm giải của từng thứ. Chạy force hoặc chạy tay upcoming thì gửi đủ mọi giải.
    var scheduled = args[0] == "daily" && !force;
    var upcomingCompetitions = scheduled
        ? competitions.Where(c => c.IsUpcomingOn(date)).ToList()
        : competitions;
    if (upcomingCompetitions.Count == 0)
        kinds = [DigestKind.Results];

    var runner = new DailyRunner(
        new EspnMatchSource(http),
        competitions,
        upcomingCompetitions,
        remindWeekend: scheduled && ReportWindow.IsWeekendReminderDay(date),
        clubs,
        new AnnouncedStore(Path.Combine(root, "data", "announced.json")),
        renderer,
        publisher,
        ignoreAnnounced: force,
        // Chạy force để test: không lưu trạng thái để lần chạy thật vẫn báo đủ.
        saveState: !dryRun && !force);

    if (force)
        await publisher.NotifyAsync($"🧪 Chạy test ({args[0]}, {date:yyyy-MM-dd}): gửi lại cả trận đã báo, không lưu trạng thái.", cts.Token);

    await runner.RunAsync(kinds, date, cts.Token);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Lỗi: {ex}");
    try
    {
        await publisher.NotifyAsync($"❌ Bot bóng đá lỗi ({args[0]}, {date:yyyy-MM-dd}): {ex.GetType().Name}: {ex.Message}", CancellationToken.None);
    }
    catch (Exception notifyError)
    {
        Console.Error.WriteLine($"Không gửi được báo lỗi: {notifyError.Message}");
    }
    return 1;
}

string LoadFont(string weight) =>
    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "assets", "fonts", $"BeVietnamPro-{weight}.ttf")));

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "config", "clubs.json")))
            return dir.FullName;
    }
    throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc chứa config/clubs.json");
}

using System.Globalization;
using FootballNews.Core.Config;
using FootballNews.Core.Live;
using FootballNews.Facebook;
using FootballNews.Rendering;
using FootballNews.Sources.Espn;
using FootballNews.Telegram;

namespace FootballNews.App;

/// <summary>Đọc cấu hình và biến môi trường cho job <c>live</c>.</summary>
internal static class LiveProgram
{
    public const string ContinueFlag = ".live-continue";

    public static async Task<int> RunAsync()
    {
        var token = Environment.GetEnvironmentVariable("LIVE_TELEGRAM_BOT_TOKEN");
        var adminText = Environment.GetEnvironmentVariable("TELEGRAM_ADMIN_ID");
        if (string.IsNullOrEmpty(token) || !long.TryParse(adminText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var adminId))
        {
            Console.Error.WriteLine("Thiếu LIVE_TELEGRAM_BOT_TOKEN hoặc TELEGRAM_ADMIN_ID (phải là số).");
            return 2;
        }

        var pageId = Environment.GetEnvironmentVariable("FB_PAGE_ID");
        var pageToken = Environment.GetEnvironmentVariable("FB_PAGE_TOKEN");
        // Mặc định chạy tối đa 5 giờ 20 phút; job GitHub giới hạn 6 giờ kể cả thời gian build.
        var maxMinutes = int.TryParse(Environment.GetEnvironmentVariable("LIVE_MAX_MINUTES"), out var m) && m > 0 ? m : 320;
        var commit = Environment.GetEnvironmentVariable("LIVE_GIT_COMMIT") == "true";

        var root = RepoRoot.Find();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var telegram = new TelegramClient(http, token, adminId.ToString(CultureInfo.InvariantCulture));
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(maxMinutes + 5));

        try
        {
            var clubs = ClubCatalog.Load(Path.Combine(root, "config", "clubs.json"));
            var config = LiveConfig.Load(Path.Combine(root, "config", "live.json"));
            var competitions = CompetitionCatalog.Load(Path.Combine(root, "config", "competitions.json"))
                .Concat(config.ExtraCompetitions)
                .ToList();
            var theme = CardTheme.Load(Path.Combine(root, "config", "card.json"));
            var html = new LiveCardHtml(theme.PageName, LoadAsset(root, "fonts", "BeVietnamPro-Bold.ttf"), LoadAsset(root, "fonts", "BeVietnamPro-Medium.ttf"), LoadAsset(root, "logo.png"));
            var facebook = string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(pageToken) ? null : new FacebookClient(http, pageId, pageToken);
            if (facebook is null)
                Console.WriteLine("Chưa cài FB_PAGE_ID/FB_PAGE_TOKEN: vẫn theo dõi và gửi bản nháp, nhưng không đăng được.");

            var runner = new LiveRunner(
                telegram,
                adminId,
                facebook,
                new EspnMatchSource(http),
                competitions,
                clubs,
                config,
                new LiveStore(Path.Combine(root, "data", "live.json")),
                new LiveCardRenderer(html),
                new GitCommitter(root, commit),
                TimeSpan.FromMinutes(maxMinutes));
            if (await runner.RunAsync(cts.Token))
            {
                // Workflow thấy file này thì tự bật lượt chạy mới (SPEC mục 13.7).
                await File.WriteAllTextAsync(Path.Combine(root, ContinueFlag), "");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lỗi: {ex}");
            try
            {
                await telegram.SendPlainAsync($"❌ Bot live lỗi: {ex.GetType().Name}: {ex.Message}", CancellationToken.None);
            }
            catch (Exception notifyError)
            {
                Console.Error.WriteLine($"Không gửi được báo lỗi: {notifyError.Message}");
            }
            return 1;
        }
    }

    /// <summary>Vẽ ảnh mẫu mỗi loại sự kiện vào <c>output/live-preview</c> để xem màu và bố cục, không gửi đi đâu.</summary>
    public static async Task<int> PreviewAsync()
    {
        var root = RepoRoot.Find();
        var clubs = ClubCatalog.Load(Path.Combine(root, "config", "clubs.json"));
        var config = LiveConfig.Load(Path.Combine(root, "config", "live.json"));
        var competition = CompetitionCatalog.Load(Path.Combine(root, "config", "competitions.json")).First(c => c.Slug == "eng.1");
        var theme = CardTheme.Load(Path.Combine(root, "config", "card.json"));
        var renderer = new LiveCardRenderer(new LiveCardHtml(theme.PageName, LoadAsset(root, "fonts", "BeVietnamPro-Bold.ttf"), LoadAsset(root, "fonts", "BeVietnamPro-Medium.ttf"), LoadAsset(root, "logo.png")));

        var home = new FootballNews.Core.Models.Team("359", "Arsenal", "https://a.espncdn.com/i/teamlogos/soccer/500/359.png");
        var away = new FootballNews.Core.Models.Team("306", "Hull City", "https://a.espncdn.com/i/teamlogos/soccer/500/306.png");
        var state = new LiveState { Watch = new WatchedMatch { Home = home, Away = away, Competition = competition.Slug } };
        var session = new LiveSession(state, clubs, competition, config.Hashtag);
        var goal = new GoalRecord { Id = "g1", TeamId = "359", Player = "Declan Rice", Minute = "23'" };
        var rice = new FootballNews.Core.Models.MatchEvent(FootballNews.Core.Models.MatchEventType.Goal, "23'", "Declan Rice", "359");
        var smith = new FootballNews.Core.Models.MatchEvent(FootballNews.Core.Models.MatchEventType.RedCard, "61'", "Smith", "306");
        var hullGoal = new FootballNews.Core.Models.MatchEvent(FootballNews.Core.Models.MatchEventType.Goal, "78'", "Oscar Estupiñán", "306");
        var saka = new FootballNews.Core.Models.MatchEvent(FootballNews.Core.Models.MatchEventType.Goal, "90'+2'", "Bukayo Saka", "359");
        var pen = new FootballNews.Core.Models.MatchEvent(FootballNews.Core.Models.MatchEventType.PenaltyGoal, "90'+6'", "Martin Ødegaard", "359");
        var samples = new (string Name, LiveEvent Event)[]
        {
            ("goal", new LiveEvent(LiveEventKind.Goal, 1, 0, goal, "359", "Declan Rice", "23'")),
            ("red-card", new LiveEvent(LiveEventKind.RedCard, 1, 0, null, "306", "Smith", "61'")),
            ("half-time", new LiveEvent(LiveEventKind.HalfTime, 1, 0, Summary: [rice, smith])),
            ("end-of-90", new LiveEvent(LiveEventKind.EndOfRegulation, 1, 1, Summary: [rice, smith, hullGoal])),
            ("full-time", new LiveEvent(LiveEventKind.FullTime, 3, 1, Summary: [rice, smith, hullGoal, saka, pen])),
            ("var", new LiveEvent(LiveEventKind.GoalCancelled, 0, 0, goal, "359", "Declan Rice", "23'")),
        };

        var dir = Path.Combine(root, "output", "live-preview");
        Directory.CreateDirectory(dir);
        foreach (var (name, e) in samples.Append(("custom", new LiveEvent(LiveEventKind.Goal, 1, 0, goal, "359", "Declan Rice", "23'"))))
        {
            var draft = session.Build(state.Watch, e);
            if (name == "custom")
                LiveSession.ApplyEdit(draft, "VÀOOOO!!!\nSiêu phẩm sút xa của Declan Rice\nArsenal 1-0 Hull City");
            var (from, to) = config.Gradient(draft.Kind);
            var png = await renderer.RenderAsync(new LiveCard(home, away, clubs.DisplayName(home), clubs.DisplayName(away),
                draft.HomeScore, draft.AwayScore, draft.Title, draft.Detail, draft.CustomLines, from, to, draft.HomeLines, draft.AwayLines,
                RedCardIcon: draft.Kind == LiveEventKind.RedCard, TeamId: draft.TeamId));
            await File.WriteAllBytesAsync(Path.Combine(dir, $"{name}.png"), png);
            await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.txt"), draft.Caption);
            Console.WriteLine($"Đã vẽ {name}.png");
        }
        return 0;
    }

    private static string LoadAsset(string root, params string[] path) =>
        Convert.ToBase64String(File.ReadAllBytes(Path.Combine([root, "assets", .. path])));
}

using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Live;

/// <summary>Kết quả một lần hỏi ESPN: bản nháp mới và bản nháp đang chờ vừa bị gắn cảnh báo VAR.</summary>
public sealed record LiveUpdate(IReadOnlyList<Draft> NewDrafts, IReadOnlyList<Draft> VarWarnings);

/// <summary>Biến sự kiện thành bản nháp theo các quy tắc ở mục 13 của SPEC.</summary>
public sealed class LiveSession(LiveState state, ClubCatalog clubs, Competition competition, string pageHashtag)
{
    public LiveUpdate Apply(Match match, DateTimeOffset now)
    {
        var watch = state.Watch ?? throw new InvalidOperationException("Chưa theo dõi trận nào.");
        var drafts = new List<Draft>();
        var warnings = new List<Draft>();

        foreach (var e in LiveTracker.Update(watch, match))
        {
            if (e.Kind == LiveEventKind.GoalCancelled)
            {
                var goalDraft = state.Drafts.FirstOrDefault(d => d.Kind == LiveEventKind.Goal && d.GoalId == e.Goal!.Id);
                if (goalDraft is null)
                    continue;
                if (goalDraft.IsPending)
                {
                    goalDraft.VarWarning = true;
                    warnings.Add(goalDraft);
                    continue;
                }
                // Chỉ báo huỷ khi bàn đó đã được đăng. Bản đã Bỏ hoặc hết hạn thì thôi.
                if (goalDraft.Status != DraftStatus.Posted)
                    continue;
            }
            drafts.Add(state.Enqueue(Build(watch, e), now));
        }
        return new LiveUpdate(drafts, warnings);
    }

    /// <summary>Chủ trang sửa nội dung: mỗi dòng là một dòng trên ảnh, cả đoạn là phần chữ của caption.</summary>
    public static void ApplyEdit(Draft draft, string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        draft.CustomLines = lines;
        draft.Text = string.Join("\n", lines);
    }

    public string HomeName(WatchedMatch w) => clubs.DisplayName(w.Home);
    public string AwayName(WatchedMatch w) => clubs.DisplayName(w.Away);

    public Draft Build(WatchedMatch w, LiveEvent e)
    {
        var home = HomeName(w);
        var away = AwayName(w);
        var score = $"{home} {e.HomeScore}-{e.AwayScore} {away}";
        var team = e.TeamId == w.Home.Id ? home : e.TeamId == w.Away.Id ? away : "";
        var player = e.Player ?? "";
        var minute = e.Minute ?? "";

        var draft = new Draft
        {
            Kind = e.Kind,
            HomeScore = e.HomeScore,
            AwayScore = e.AwayScore,
            GoalId = e.Goal?.Id,
            Hashtags = string.Join(' ', new[] { clubs.Hashtag(w.Home), clubs.Hashtag(w.Away), competition.Hashtag, pageHashtag }
                .Where(t => t.Length > 0)
                .Distinct()
                .Select(t => "#" + t)),
        };

        switch (e.Kind)
        {
            case LiveEventKind.Goal:
                var goalType = e.Goal?.Type ?? MatchEventType.Goal;
                var note = goalType switch
                {
                    MatchEventType.PenaltyGoal => " (phạt đền)",
                    MatchEventType.OwnGoal => " (phản lưới)",
                    _ => "",
                };
                draft.Title = "BÀN THẮNG!";
                draft.Detail = player.Length > 0 ? Join(player, minute) + note : null;
                var opener = e.HomeScore + e.AwayScore == 1;
                draft.Text = player.Length == 0
                    ? $"⚽ {team} ghi bàn! {score}"
                    : goalType == MatchEventType.OwnGoal
                        ? $"⚽ {player} phản lưới nhà, {team} có bàn thắng! {score}"
                        : $"⚽ {Join(player, minute)}{note} {(opener ? "mở tỉ số" : "ghi bàn")} cho {team}! {score}";
                break;

            case LiveEventKind.RedCard:
                draft.Title = "THẺ ĐỎ";
                draft.Detail = player.Length > 0 ? $"{Join(player, minute)} ({team})" : team;
                draft.Text = player.Length > 0
                    ? $"🟥 {player} ({team}) nhận thẻ đỏ{(minute.Length > 0 ? $" phút {minute}" : "")}! {score}"
                    : $"🟥 {team} nhận thẻ đỏ! {score}";
                break;

            case LiveEventKind.HalfTime:
                draft.Title = "HẾT HIỆP 1";
                draft.Text = $"⏸️ Hết hiệp 1: {score}";
                break;

            case LiveEventKind.EndOfRegulation:
                draft.Title = "HẾT 90 PHÚT";
                draft.Detail = "Hai đội bước vào hiệp phụ";
                draft.Text = $"⏱️ Hết 90 phút: {score}. Hai đội bước vào hiệp phụ!";
                break;

            case LiveEventKind.FullTime:
                draft.Title = e.ExtraTime ? "HẾT HIỆP PHỤ" : "KẾT THÚC";
                draft.Text = e.ExtraTime ? $"🏁 Hết hiệp phụ: {score}" : $"🏁 Kết thúc: {score}";
                break;

            case LiveEventKind.GoalCancelled:
                draft.Title = "VAR HUỶ BÀN THẮNG";
                draft.Detail = player.Length > 0 ? $"Bàn của {Join(player, minute)}" : $"Bàn của {team}";
                draft.Text = $"❌ VAR huỷ bàn thắng {(player.Length > 0 ? $"của {Join(player, minute)}" : $"của {team}")}. Tỉ số trở lại {score}";
                break;
        }
        return draft;
    }

    public static string KindLabel(LiveEventKind kind) => kind switch
    {
        LiveEventKind.Goal => "⚽ Bàn thắng",
        LiveEventKind.RedCard => "🟥 Thẻ đỏ",
        LiveEventKind.HalfTime => "⏸️ Hết hiệp 1",
        LiveEventKind.EndOfRegulation => "⏱️ Hết 90 phút",
        LiveEventKind.FullTime => "🏁 Kết thúc",
        LiveEventKind.GoalCancelled => "❌ VAR huỷ bàn thắng",
        _ => kind.ToString(),
    };

    private static string Join(string player, string minute) => minute.Length > 0 ? $"{player} {minute}" : player;
}

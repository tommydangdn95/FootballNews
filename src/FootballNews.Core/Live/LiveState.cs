using System.Text.Json.Serialization;
using FootballNews.Core.Models;

namespace FootballNews.Core.Live;

public enum LiveEventKind { Goal, RedCard, HalfTime, EndOfRegulation, FullTime, GoalCancelled }

public enum DraftStatus { Queued, Active, Posted, Skipped, Expired, Cancelled }

/// <summary>Một bàn thắng bot đã thấy. <see cref="Cancelled"/> khi số bàn của đội giảm (VAR huỷ).</summary>
public sealed class GoalRecord
{
    public string Id { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string Player { get; set; } = "";
    public string Minute { get; set; } = "";
    public MatchEventType Type { get; set; } = MatchEventType.Goal;
    public bool Cancelled { get; set; }
}

/// <summary>Trận đang theo dõi và những gì bot đã thấy của trận đó.</summary>
public sealed class WatchedMatch
{
    public string MatchId { get; set; } = "";
    public string Competition { get; set; } = "";
    public DateTimeOffset KickoffUtc { get; set; }
    public Team Home { get; set; } = new("", "", null);
    public Team Away { get; set; } = new("", "", null);

    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public List<GoalRecord> Goals { get; set; } = [];
    public List<string> RedCards { get; set; } = [];
    public bool HalfTimeDone { get; set; }
    public bool EndOfRegulationDone { get; set; }
    public bool HadExtraTime { get; set; }
    public bool FullTimeDone { get; set; }
    public int NextGoalId { get; set; } = 1;

    /// <summary>Chủ trang bật /autopilot: bản nháp tự đăng lên Fanpage, không chờ duyệt. Tắt khi bỏ theo dõi.</summary>
    public bool Autopilot { get; set; }

    /// <summary>Đã báo hết trận: chỉ còn chờ xử lý xong hàng đợi bản nháp.</summary>
    [JsonIgnore]
    public bool Finished => FullTimeDone;
}

/// <summary>Bản nháp một bài đăng. Giữ đúng tỉ số lúc xảy ra sự kiện.</summary>
public sealed class Draft
{
    public int Id { get; set; }
    public LiveEventKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DraftStatus Status { get; set; } = DraftStatus.Queued;

    public int HomeScore { get; set; }
    public int AwayScore { get; set; }

    /// <summary>Dòng tiêu đề trên ảnh, vd "VÀOOOOO".</summary>
    public string Title { get; set; } = "";

    /// <summary>Dòng chi tiết trên ảnh dưới tỉ số, vd "Declan Rice 23'".</summary>
    public string? Detail { get; set; }

    /// <summary>Cuối một dòng dưới tên đội: dòng đó là thẻ đỏ, ảnh vẽ hình thẻ đỏ thay cho ký hiệu này.</summary>
    public const string RedCardMark = "🟥";

    /// <summary>Đội của sự kiện (bài thẻ đỏ vẽ logo đội này thay cho tỉ số).</summary>
    public string? TeamId { get; set; }

    /// <summary>Các dòng nhỏ dưới tên đội nhà trên ảnh: người ghi bàn, thẻ đỏ (bài hết hiệp, hết trận).</summary>
    public List<string>? HomeLines { get; set; }

    /// <summary>Như <see cref="HomeLines"/>, cho đội khách.</summary>
    public List<string>? AwayLines { get; set; }

    /// <summary>Chủ trang sửa nội dung thì ảnh chỉ vẽ các dòng này thay cho tiêu đề, tỉ số và chi tiết.</summary>
    public List<string>? CustomLines { get; set; }

    /// <summary>Phần chữ của caption, chưa có hashtag.</summary>
    public string Text { get; set; } = "";
    public string Hashtags { get; set; } = "";

    /// <summary>Bàn thắng của bản nháp (bàn thắng hoặc VAR huỷ).</summary>
    public string? GoalId { get; set; }

    /// <summary>Bàn của bản nháp này đã bị VAR huỷ trong lúc bản nháp còn chờ.</summary>
    public bool VarWarning { get; set; }

    public long? TelegramMessageId { get; set; }
    public bool TelegramIsPhoto { get; set; }
    public string? FacebookPostId { get; set; }

    [JsonIgnore]
    public string Caption => Hashtags.Length == 0 ? Text : $"{Text}\n\n{Hashtags}";

    [JsonIgnore]
    public bool IsPending => Status is DraftStatus.Queued or DraftStatus.Active;
}

public sealed class LiveState
{
    public WatchedMatch? Watch { get; set; }
    public List<Draft> Drafts { get; set; } = [];
    public int NextDraftId { get; set; } = 1;

    /// <summary>Bản nháp chủ trang vừa bấm ✏️ Sửa, đang chờ nội dung mới.</summary>
    public int? EditingDraftId { get; set; }

    [JsonIgnore]
    public Draft? Active => Drafts.FirstOrDefault(d => d.Status == DraftStatus.Active);

    public Draft? Find(int id) => Drafts.FirstOrDefault(d => d.Id == id);

    public Draft? FindByMessage(long messageId) => Drafts.FirstOrDefault(d => d.TelegramMessageId == messageId);

    [JsonIgnore]
    public bool HasPending => Drafts.Any(d => d.IsPending);

    /// <summary>Các bản nháp đang chờ đã quá hạn, tính từ lúc bot phát hiện sự kiện. Đổi trạng thái thành Expired.</summary>
    public IReadOnlyList<Draft> ExpireDue(DateTimeOffset now, TimeSpan lifetime)
    {
        var expired = Drafts.Where(d => d.IsPending && now - d.CreatedAt >= lifetime).ToList();
        foreach (var draft in expired)
        {
            draft.Status = DraftStatus.Expired;
            if (EditingDraftId == draft.Id)
                EditingDraftId = null;
        }
        return expired;
    }

    /// <summary>Chưa có bản nào đang chờ quyết định thì đưa bản đầu hàng đợi lên.</summary>
    public Draft? PromoteNext()
    {
        if (Active is not null)
            return null;
        var next = Drafts.Where(d => d.Status == DraftStatus.Queued).OrderBy(d => d.Id).FirstOrDefault();
        if (next is not null)
            next.Status = DraftStatus.Active;
        return next;
    }

    public Draft Enqueue(Draft draft, DateTimeOffset now)
    {
        draft.Id = NextDraftId++;
        draft.CreatedAt = now;
        draft.Status = DraftStatus.Queued;
        Drafts.Add(draft);
        return draft;
    }

    /// <summary>Bỏ theo dõi hoặc theo dõi xong: dọn trận và các bản nháp.</summary>
    public void Clear()
    {
        Watch = null;
        Drafts.Clear();
        EditingDraftId = null;
    }
}

namespace FootballNews.Core.Models;

public enum DigestKind { Results, Upcoming }

/// <summary>Nội dung của một bài đăng. Updates là các trận đã báo nhưng đổi giờ, hoãn hoặc huỷ.</summary>
public sealed record Digest(DigestKind Kind, IReadOnlyList<Match> Matches, IReadOnlyList<Match> Updates)
{
    public bool IsEmpty => Matches.Count == 0 && Updates.Count == 0;
}

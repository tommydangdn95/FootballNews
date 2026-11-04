using FootballNews.Core.Filtering;
using FootballNews.Core.Models;

namespace FootballNews.Core.Dedup;

public sealed record UpcomingEntry(DateTimeOffset KickoffUtc, MatchStatus Status, string Competition, DateTimeOffset AnnouncedAt);

public sealed record ResultEntry(DateTimeOffset AnnouncedAt);

public sealed class AnnouncedState
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public Dictionary<string, UpcomingEntry> Upcoming { get; init; } = [];
    public Dictionary<string, ResultEntry> Results { get; init; } = [];

    public Digest BuildResults(IEnumerable<Match> matches, ReportWindow window)
    {
        var fresh = matches
            .Where(m => m.Status == MatchStatus.Finished && window.Contains(m.KickoffUtc) && !Results.ContainsKey(m.Id))
            .ToList();
        return new Digest(DigestKind.Results, fresh, []);
    }

    /// <param name="matches">Các trận lấy theo khung giờ.</param>
    /// <param name="rechecked">Các trận đã báo được lấy lại theo id, có thể đã dời ra ngoài khung giờ.</param>
    /// <param name="window">Khung giờ của bài lịch đấu.</param>
    public Digest BuildUpcoming(IEnumerable<Match> matches, IEnumerable<Match> rechecked, ReportWindow window)
    {
        var fresh = new List<Match>();
        var updates = new List<Match>();

        foreach (var match in matches.Where(m => window.Contains(m.KickoffUtc)))
        {
            if (Upcoming.ContainsKey(match.Id))
            {
                if (HasChanged(match))
                    updates.Add(match);
            }
            else if (match.Status == MatchStatus.Scheduled)
            {
                fresh.Add(match);
            }
            else if (match.Status is MatchStatus.Postponed or MatchStatus.Cancelled)
            {
                updates.Add(match);
            }
        }

        var known = updates.Select(m => m.Id).ToHashSet();
        updates.AddRange(rechecked.Where(m => !known.Contains(m.Id) && HasChanged(m)));

        return new Digest(DigestKind.Upcoming, fresh, updates);
    }

    /// <summary>Các trận đã báo lịch, chưa báo kết quả và chưa bị huỷ.</summary>
    public IEnumerable<(string MatchId, string Competition)> PendingRechecks() => Upcoming
        .Where(e => !Results.ContainsKey(e.Key) && e.Value.Status != MatchStatus.Cancelled)
        .Select(e => (e.Key, e.Value.Competition));

    public void MarkAnnounced(Digest digest, DateTimeOffset now)
    {
        foreach (var match in digest.Matches.Concat(digest.Updates))
        {
            if (digest.Kind == DigestKind.Results)
                Results[match.Id] = new ResultEntry(now);
            else
                Upcoming[match.Id] = new UpcomingEntry(match.KickoffUtc, match.Status, match.Competition.Slug, now);
        }
    }

    public void Purge(DateTimeOffset now)
    {
        var cutoff = now - Retention;
        foreach (var id in Upcoming.Where(e => e.Value.AnnouncedAt < cutoff).Select(e => e.Key).ToList())
            Upcoming.Remove(id);
        foreach (var id in Results.Where(e => e.Value.AnnouncedAt < cutoff).Select(e => e.Key).ToList())
            Results.Remove(id);
    }

    // Trận đang đá hoặc đã xong không phải là thay đổi lịch: phần kết quả sẽ báo các trận này.
    private bool HasChanged(Match match) =>
        match.Status is MatchStatus.Scheduled or MatchStatus.Postponed or MatchStatus.Cancelled
        && Upcoming.TryGetValue(match.Id, out var entry)
        && (entry.Status != match.Status || entry.KickoffUtc != match.KickoffUtc);
}

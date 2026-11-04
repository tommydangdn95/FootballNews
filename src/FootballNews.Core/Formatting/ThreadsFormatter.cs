using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Formatting;

public sealed class ThreadsFormatter(ClubCatalog clubs)
{
    public const int MaxLength = 500;
    private const int MaxHashtags = 3;
    private readonly MatchText _text = new(clubs);

    public Draft Format(Digest digest)
    {
        var hashtags = _text.Hashtags(digest.Matches.Concat(digest.Updates), MaxHashtags);
        var blocks = digest.Updates.Select(m => "⚠️ " + _text.UpdateLine(m))
            .Concat(clubs.ByHighlight(digest.Matches).Select(m => MatchBlock(digest.Kind, m)))
            .ToList();

        var body = _text.Title(digest.Kind);
        var used = 0;
        foreach (var block in blocks)
        {
            var remaining = blocks.Count - used - 1;
            var suffix = remaining > 0 ? $"\n\n…và {remaining} trận khác" : "";
            if (Compose(body + "\n\n" + block + suffix, hashtags).Length > MaxLength)
                break;
            body += "\n\n" + block;
            used++;
        }

        if (used < blocks.Count)
            body += $"\n\n…và {blocks.Count - used} trận khác";
        return new Draft("Threads", [Compose(body, hashtags)]);
    }

    private string MatchBlock(DigestKind kind, Match match)
    {
        if (kind == DigestKind.Upcoming)
            return _text.KickoffLine(match);

        var goals = match.Events.Where(e => e.IsGoal).Select(MatchText.GoalText).ToList();
        return goals.Count == 0
            ? _text.ScoreLine(match)
            : $"{_text.ScoreLine(match)}\n⚽ {string.Join(", ", goals)}";
    }

    private static string Compose(string body, string hashtags) => $"{body}\n\n{hashtags}";
}

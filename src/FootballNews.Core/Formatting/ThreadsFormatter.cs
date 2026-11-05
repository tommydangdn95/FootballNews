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
            .Concat(_text.Ordered(digest).Select(m => MatchBlock(digest.Kind, m)))
            .ToList();

        // Mỗi bài đều chừa chỗ cho hashtag, vì bài cuối mới biết sau khi xếp xong.
        var limit = MaxLength - Compose("", hashtags).Length;
        var parts = new List<string>();
        var body = _text.Title(digest.Kind);
        foreach (var block in blocks)
        {
            if (body.Length + 2 + block.Length > limit)
            {
                parts.Add(body);
                body = block;
            }
            else
            {
                body += "\n\n" + block;
            }
        }
        parts.Add(Compose(body, hashtags));
        return new Draft("Threads", parts);
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

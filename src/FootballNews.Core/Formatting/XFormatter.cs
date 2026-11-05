using System.Text;
using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Formatting;

public sealed class XFormatter(ClubCatalog clubs)
{
    public const int MaxWeightedLength = 280;
    private const int MaxHashtags = 2;
    private readonly MatchText _text = new(clubs);

    public Draft Format(Digest digest)
    {
        var ordered = _text.Ordered(digest).ToList();
        var lines = new List<string> { _text.Title(digest), "" };
        lines.AddRange(digest.Updates.Select(m => "⚠️ " + _text.UpdateLine(m)));
        lines.AddRange(ordered.Select(m => digest.Kind == DigestKind.Results ? _text.ScoreLine(m) : _text.KickoffLine(m)));

        // Mỗi tweet đều chừa chỗ cho hashtag để tweet cuối không phải tách riêng hashtag.
        var suffix = "\n\n" + _text.Hashtags(ordered.Concat(digest.Updates), MaxHashtags);
        var tweets = SplitIntoTweets(lines, MaxWeightedLength - WeightedLength(suffix));
        tweets[^1] += suffix;
        return new Draft("X", tweets);
    }

    /// <summary>
    /// Độ dài theo cách X tính: Latin cơ bản tính 1, còn lại tính 2.
    /// Nhiều chữ tiếng Việt có dấu (U+1EA0–U+1EF9) và emoji đều tính 2.
    /// </summary>
    public static int WeightedLength(string text)
    {
        var total = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            var isLight = v <= 4351 || v is >= 8192 and <= 8205 or >= 8208 and <= 8223 or >= 8242 and <= 8247;
            total += isLight ? 1 : 2;
        }
        return total;
    }

    private static List<string> SplitIntoTweets(IEnumerable<string> lines, int maxLength)
    {
        var tweets = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (current.Length > 0 && WeightedLength(current + "\n" + line) > maxLength)
            {
                tweets.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0)
                current.Append('\n');
            current.Append(line);
        }
        if (current.ToString().Trim().Length > 0)
            tweets.Add(current.ToString().Trim());
        return tweets;
    }
}

using System.Text;
using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Formatting;

public sealed class FacebookFormatter(ClubCatalog clubs)
{
    private const int MaxHashtags = 6;
    private readonly MatchText _text = new(clubs);

    public Draft Format(Digest digest)
    {
        var sb = new StringBuilder(_text.Title(digest.Kind)).Append('\n');

        if (digest.Updates.Count > 0)
        {
            sb.Append("\n⚠️ CẬP NHẬT\n");
            foreach (var match in digest.Updates)
                sb.Append(_text.UpdateLine(match)).Append('\n');
        }

        var groups = digest.Matches.GroupBy(m => m.Competition).OrderBy(g => g.Key.Order);
        foreach (var group in groups)
        {
            sb.Append($"\n{group.Key.Emoji} {group.Key.Name}\n");
            var ordered = group.OrderBy(m => m.KickoffUtc).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0 && digest.Kind == DigestKind.Results)
                    sb.Append('\n');
                AppendMatch(sb, digest.Kind, ordered[i]);
            }
        }

        sb.Append('\n').Append(_text.Hashtags(digest.Matches.Concat(digest.Updates), MaxHashtags));
        return new Draft("Facebook", [sb.ToString().TrimEnd()]);
    }

    private void AppendMatch(StringBuilder sb, DigestKind kind, Match match)
    {
        if (kind == DigestKind.Upcoming)
        {
            sb.Append(_text.KickoffLine(match)).Append('\n');
            if (_text.FormLine(match) is { } form)
                sb.Append("   ").Append(form).Append('\n');
            return;
        }

        sb.Append(_text.ScoreLine(match)).Append('\n');
        foreach (var goal in match.Events.Where(e => e.IsGoal))
            sb.Append("⚽ ").Append(MatchText.GoalText(goal)).Append('\n');
        foreach (var red in match.Events.Where(e => e.Type == MatchEventType.RedCard))
            sb.Append($"🟥 {red.Player} {red.Minute}\n");

        var homeYellow = match.Events.Count(e => e.Type == MatchEventType.YellowCard && e.TeamId == match.Home.Team.Id);
        var awayYellow = match.Events.Count(e => e.Type == MatchEventType.YellowCard && e.TeamId == match.Away.Team.Id);
        if (homeYellow + awayYellow > 0)
            sb.Append($"🟨 {_text.Name(match.Home)} {homeYellow} – {_text.Name(match.Away)} {awayYellow}\n");
    }
}

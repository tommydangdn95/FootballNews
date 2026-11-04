using FootballNews.Core.Config;
using FootballNews.Core.Models;

namespace FootballNews.Core.Filtering;

public static class BigClubFilter
{
    public static IEnumerable<Match> Apply(IEnumerable<Match> matches, ClubCatalog clubs) =>
        matches.Where(clubs.Involves);
}

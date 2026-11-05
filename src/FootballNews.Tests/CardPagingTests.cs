using FootballNews.Rendering;

namespace FootballNews.Tests;

public class CardPagingTests
{
    private static List<int>[] Groups(params int[] sizes)
    {
        var next = 0;
        return sizes.Select(size => Enumerable.Range(next, size).Select(_ => next++).ToList()).ToArray();
    }

    private static int[] PageSizes(params int[] groupSizes) =>
        CardPaging.Paginate(Groups(groupSizes)).Select(p => p.Length).ToArray();

    [Theory]
    [InlineData(new[] { 6 }, new[] { 6 })]
    [InlineData(new[] { 7 }, new[] { 4, 3 })]
    [InlineData(new[] { 13 }, new[] { 5, 4, 4 })]
    [InlineData(new[] { 18 }, new[] { 6, 6, 6 })]
    public void Large_competition_is_split_evenly(int[] groups, int[] expected)
    {
        Assert.Equal(expected, PageSizes(groups));
    }

    [Fact]
    public void Each_competition_gets_its_own_pages()
    {
        Assert.Equal([3, 2, 5], PageSizes(3, 2, 5));
    }

    [Theory]
    [InlineData(new[] { 1, 5 }, new[] { 6 })]
    [InlineData(new[] { 5, 1 }, new[] { 6 })]
    [InlineData(new[] { 6, 1 }, new[] { 4, 3 })]
    [InlineData(new[] { 1, 1 }, new[] { 2 })]
    [InlineData(new[] { 1, 1, 1 }, new[] { 3 })]
    public void Lone_match_shares_a_page_with_a_neighbour(int[] groups, int[] expected)
    {
        Assert.Equal(expected, PageSizes(groups));
    }

    [Fact]
    public void Sections_keep_the_pages_of_one_competition_together()
    {
        var sections = CardPaging.PaginateSections(Groups(7, 3));

        Assert.Equal([2, 1], sections.Select(s => s.Count));
    }

    [Fact]
    public void Single_match_overall_still_has_a_page()
    {
        Assert.Equal([1], PageSizes(1));
        Assert.Empty(PageSizes());
    }

    [Fact]
    public void Every_combination_keeps_all_matches_in_order_within_page_limits()
    {
        for (var a = 0; a <= 20; a++)
        for (var b = 0; b <= 20; b++)
        for (var c = 0; c <= 20; c++)
        {
            var groups = Groups(a, b, c);
            var pages = CardPaging.Paginate(groups);

            var min = Math.Min(a + b + c, CardPaging.MinPerPage);
            Assert.All(pages, p => Assert.InRange(p.Length, min, CardPaging.MaxPerPage));
            Assert.Equal(groups.SelectMany(g => g), pages.SelectMany(p => p));
        }
    }
}

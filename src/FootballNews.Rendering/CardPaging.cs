namespace FootballNews.Rendering;

/// <summary>
/// Chia các trận thành từng ảnh: mỗi giải ảnh riêng, mỗi ảnh có từ 2 đến 6 trận.
/// Ngoại lệ duy nhất là cả digest chỉ có 1 trận: vẫn vẽ một ảnh cho trận đó.
/// </summary>
public static class CardPaging
{
    public const int MinPerPage = 2;
    public const int MaxPerPage = 6;

    public static IReadOnlyList<T[]> Paginate<T>(IEnumerable<IReadOnlyList<T>> groups) =>
        MergeLoneItems(groups).SelectMany(SplitEvenly).ToList();

    // Giải chỉ có 1 trận không đủ một ảnh riêng nên đi chung với giải liền kề.
    private static List<List<T>> MergeLoneItems<T>(IEnumerable<IReadOnlyList<T>> groups)
    {
        var merged = new List<List<T>>();
        foreach (var group in groups.Where(g => g.Count > 0))
        {
            if (merged.Count > 0 && merged[^1].Count < MinPerPage)
                merged[^1].AddRange(group);
            else
                merged.Add([.. group]);
        }

        if (merged.Count > 1 && merged[^1].Count < MinPerPage)
        {
            merged[^2].AddRange(merged[^1]);
            merged.RemoveAt(merged.Count - 1);
        }
        return merged;
    }

    // Chia đều thay vì lấp đầy từng ảnh, nên 7 trận thành 4 + 3 chứ không phải 6 + 1.
    private static IEnumerable<T[]> SplitEvenly<T>(List<T> items)
    {
        var pageCount = (items.Count + MaxPerPage - 1) / MaxPerPage;
        var start = 0;
        for (var page = 0; page < pageCount; page++)
        {
            var size = items.Count / pageCount + (page < items.Count % pageCount ? 1 : 0);
            yield return items.GetRange(start, size).ToArray();
            start += size;
        }
    }
}

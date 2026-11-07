using System.Globalization;
using System.Text;

namespace FootballNews.Core.Formatting;

public static class Hashtags
{
    /// <summary>"Hull City" thành "HullCity", "Atlético Madrid" thành "AtleticoMadrid".</summary>
    public static string FromName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in TextSearch.RemoveDiacritics(name))
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }
}

public static class TextSearch
{
    /// <summary>Bỏ dấu tiếng Việt và dấu Latin, đổi "đ" thành "d".</summary>
    public static string RemoveDiacritics(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Chữ thường, không dấu, để so khớp từ khoá do người dùng gõ.</summary>
    public static string Normalize(string text) => RemoveDiacritics(text).ToLowerInvariant();

    /// <summary>Mọi từ trong câu tìm đều có mặt trong <paramref name="haystack"/>.</summary>
    public static bool MatchesAllWords(string query, string haystack)
    {
        var target = Normalize(haystack);
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(target.Contains);
    }
}

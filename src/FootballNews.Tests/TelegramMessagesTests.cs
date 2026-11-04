using FootballNews.Core.Formatting;
using FootballNews.Telegram;

namespace FootballNews.Tests;

public class TelegramMessagesTests
{
    [Fact]
    public void Escapes_html_and_wraps_in_pre()
    {
        var message = Assert.Single(TelegramMessages.Build(new Draft("Facebook", ["KẾT QUẢ ĐÊM QUA & <SÁNG>"])));

        Assert.StartsWith("<b>Facebook</b>\n<pre>", message);
        Assert.Contains("&amp;", message);
        Assert.Contains("&lt;", message);
        Assert.Contains("&gt;", message);
    }

    [Fact]
    public void Long_text_is_split_on_lines_without_losing_content()
    {
        var text = string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"Dòng số {i}"));

        var chunks = TelegramMessages.Split(text, TelegramMessages.MaxChunkLength).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= TelegramMessages.MaxChunkLength));
        Assert.Equal(text, string.Join('\n', chunks));
    }

    [Fact]
    public void Single_long_line_is_cut_without_breaking_emoji()
    {
        var text = string.Concat(Enumerable.Repeat("⚽x😀", 3000));

        var chunks = TelegramMessages.Split(text, 100).ToList();

        Assert.All(chunks, c => Assert.True(c.Length <= 100));
        Assert.All(chunks, c => Assert.False(char.IsHighSurrogate(c[^1])));
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void Multi_part_drafts_get_numbered_labels()
    {
        var messages = TelegramMessages.Build(new Draft("X", ["một", "hai"])).ToList();

        Assert.StartsWith("<b>X (1/2)</b>", messages[0]);
        Assert.StartsWith("<b>X (2/2)</b>", messages[1]);
    }
}

using System.Net;
using FootballNews.Core.Models;
using FootballNews.Core.Sources;

namespace FootballNews.Sources.Espn;

public sealed class EspnMatchSource(HttpClient http) : IMatchSource
{
    private const string BaseUrl = "https://site.api.espn.com/apis/site/v2/sports/soccer";
    private const int MaxAttempts = 3;

    public async Task<IReadOnlyList<Match>> GetMatchesAsync(Competition competition, DateOnly date, CancellationToken ct)
    {
        var json = await GetAsync($"{BaseUrl}/{competition.Slug}/scoreboard?dates={date:yyyyMMdd}", ct);
        return EspnParser.ParseScoreboard(json, competition);
    }

    public async Task<Match?> GetMatchAsync(Competition competition, string matchId, CancellationToken ct)
    {
        var json = await GetAsync($"{BaseUrl}/{competition.Slug}/summary?event={matchId}", ct);
        return EspnParser.ParseSummary(json, matchId, competition);
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(ct);
                }
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && ex is HttpRequestException { StatusCode: null } or TaskCanceledException)
            {
                Console.WriteLine($"ESPN lỗi tạm thời, thử lại lần {attempt + 1}: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;
}

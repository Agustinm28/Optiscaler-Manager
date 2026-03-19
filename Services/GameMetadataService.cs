using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OptiscalerClient.Services;

public class GameMetadataService
{
    private readonly HttpClient _httpClient;

    public GameMetadataService()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Searches the Steam Store API for the game by name and returns the URL for its library poster image.
    /// </summary>
    public async Task<string?> FetchCoverImageUrlAsync(string gameName)
    {
        try
        {
            // Simple sanitization to improve search results
            string queryName = Uri.EscapeDataString(gameName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    // Get the first matching game's AppID
                    var firstItem = items[0];
                    if (firstItem.TryGetProperty("id", out var idEl))
                    {
                        int appId = idEl.GetInt32();
                        return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg";
                    }
                }
            }
        }
        catch
        {
            // Ignore network or parsing errors and just return null (no cover art)
        }

        return null;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GogDisc.Core;

public enum ArtworkKind { Cover, Background, Icon }
public sealed record ArtworkGame(long Id, string Name);
public sealed record ArtworkAuthor(string Name);
public sealed record ArtworkOption(long Id, string Url, string Thumb, int Width, int Height, ArtworkAuthor? Author)
{
    public string Caption => $"{Width} × {Height} · {Author?.Name ?? "Unknown artist"}";
}

public sealed class SteamGridDbClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<List<ArtworkGame>> SearchAsync(string title, string key, CancellationToken token) =>
        GetAsync<ArtworkGame>("search/autocomplete/" + Uri.EscapeDataString(title.Trim()), key, token);

    public async Task<List<ArtworkOption>> ArtworkAsync(long gameId, ArtworkKind kind, int page, string key, CancellationToken token)
    {
        if (gameId <= 0 || page < 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        var category = kind switch { ArtworkKind.Cover => "grids", ArtworkKind.Background => "heroes", ArtworkKind.Icon => "icons", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        var dimensions = kind == ArtworkKind.Cover ? "&dimensions=600x900,342x482,660x930" : "";
        // The icons endpoint rejects image/jpeg, even when PNG is also requested.
        var mimes = kind == ArtworkKind.Icon ? "image/png" : "image/png,image/jpeg";
        var art = await GetAsync<ArtworkOption>($"{category}/game/{gameId}?page={page}&types=static&mimes={mimes}&nsfw=false&epilepsy=false{dimensions}", key, token);
        var supported = art.Where(item => IsArtworkUrl(item.Url) && IsArtworkUrl(item.Thumb)).ToList();
        if (art.Count > 0 && supported.Count == 0)
            throw new InvalidDataException("SteamGridDB found artwork, but its image addresses are not supported by this version. Update the app or choose a local file.");
        return supported;
    }

    public static bool IsArtworkUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" &&
        (uri.Host.Equals("cdn.steamgriddb.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("cdn2.steamgriddb.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    public async Task<byte[]> DownloadAsync(string url, CancellationToken token)
    {
        if (!IsArtworkUrl(url)) throw new InvalidDataException("SteamGridDB returned an unsupported artwork URL.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await ReadAsync(request, 24 * 1024 * 1024, token);
    }

    private async Task<List<T>> GetAsync<T>(string route, string key, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter your SteamGridDB API key to search online, or choose a local file.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        var bytes = await ReadAsync(request, 4 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("SteamGridDB could not complete this request.");
        return document.RootElement.GetProperty("data").Deserialize<List<T>>(JsonOptions) ?? [];
    }

    private async Task<byte[]> ReadAsync(HttpRequestMessage request, int limit, CancellationToken token)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("SteamGridDB rejected the request. Check your API key and account access.");
        if ((int)response.StatusCode == 429) throw new InvalidOperationException("SteamGridDB's request limit was reached. Try again later.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The artwork response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("The artwork response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

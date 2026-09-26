using System.Net;
using System.Text;
using GogDisc.Core;

internal static class ArtworkChecks
{
    public static async Task Run()
    {
        var requests = new List<(Uri Url, string? Auth)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            if (request.RequestUri!.Host is "cdn.steamgriddb.com" or "cdn2.steamgriddb.com")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            var json = request.RequestUri.AbsolutePath.Contains("autocomplete")
                ? """{"success":true,"data":[{"id":42,"name":"Game & More"}]}"""
                : """{"success":true,"data":[{"id":7,"url":"https://cdn2.steamgriddb.com/grid/full.png","thumb":"https://cdn2.steamgriddb.com/grid/thumb.png","width":600,"height":900,"author":{"name":"Artist"}},{"id":8,"url":"http://127.0.0.1/private","thumb":"https://cdn.steamgriddb.com/grid/thumb.png"}]}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var api = new SteamGridDbClient(client);
        var games = await api.SearchAsync("Game & More", "test-token", default);
        Check(games.Single().Id == 42, "Game response parsing failed.");
        Check(requests[0].Url.AbsoluteUri.Contains("Game%20%26%20More"), "Search query was not encoded.");
        foreach (var kind in Enum.GetValues<ArtworkKind>())
        {
            var options = await api.ArtworkAsync(42, kind, 2, "test-token", default);
            Check(options.Count == 1 && options[0].Caption.Contains("Artist"), "Artwork parsing or URL validation failed.");
            Check(requests[^1].Url.Query.Contains("page=2"), "Artwork page was ignored.");
            var route = kind == ArtworkKind.Cover ? "grids" : kind == ArtworkKind.Background ? "heroes" : "icons";
            Check(requests[^1].Url.AbsolutePath.Contains($"/{route}/game/42"), "Wrong artwork category.");
            Check(requests[^1].Url.Query.Contains(kind == ArtworkKind.Icon ? "mimes=image/png&" : "mimes=image/png,image/jpeg&"), "Wrong MIME filter for artwork category.");
        }
        await api.DownloadAsync("https://cdn.steamgriddb.com/grid/full.png", default);
        Check(requests.Take(4).All(request => request.Auth == "Bearer test-token"), "API authentication is missing.");
        Check(requests[^1].Auth is null, "API key leaked to image download.");
        await api.DownloadAsync("https://cdn2.steamgriddb.com/grid/full.png", default);
        Check(requests[^1].Auth is null, "API key leaked to the second CDN.");
        foreach (var url in new[] { "file:///C:/private", "https://cdn.steamgriddb.com.attacker.test/x", "https://user@cdn.steamgriddb.com/x", "http://cdn.steamgriddb.com/x", "https://cdn2.steamgriddb.com.attacker.test/x", "https://user@cdn2.steamgriddb.com/x", "http://cdn2.steamgriddb.com/x", "https://cdn2.steamgriddb.com:8443/x" })
        {
            Check(!SteamGridDbClient.IsArtworkUrl(url), "Unsafe image URL accepted.");
            await Reject<InvalidDataException>(() => api.DownloadAsync(url, default));
        }
        await Reject<InvalidOperationException>(() => api.SearchAsync("Game", "", default));
        using var unsupported = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"data":[{"id":9,"url":"https://unknown.example/art.png","thumb":"https://unknown.example/thumb.png"}]}""")
        }));
        await Reject<InvalidDataException>(() => new SteamGridDbClient(unsupported).ArtworkAsync(42, ArtworkKind.Background, 0, "test-token", default));
        using var empty = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"data":[]}""")
        }));
        Check((await new SteamGridDbClient(empty).ArtworkAsync(42, ArtworkKind.Background, 0, "test-token", default)).Count == 0, "A genuinely empty result should remain empty.");
        foreach (var code in new[] { HttpStatusCode.Unauthorized, (HttpStatusCode)429 })
        {
            using var failed = new HttpClient(new StubHandler(_ => new(code)));
            await Reject<InvalidOperationException>(() => new SteamGridDbClient(failed).SearchAsync("Game", "test-token", default));
        }
        using var oversized = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
            response.Content.Headers.ContentLength = 30 * 1024 * 1024;
            return response;
        }));
        await Reject<InvalidDataException>(() => new SteamGridDbClient(oversized).DownloadAsync("https://cdn.steamgriddb.com/grid/full.png", default));
    }

    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

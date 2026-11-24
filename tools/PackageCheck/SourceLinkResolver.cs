namespace Broadside.Tools.PackageCheck;

/// <summary>Fetches SourceLink URLs, the way a debugger would, and reports the ones that do not resolve.</summary>
public static class SourceLinkResolver
{
    /// <summary>Requests every URL (HEAD, eight at a time); returns <c>url: status</c> for each one that is not a success.</summary>
    public static async Task<IReadOnlyList<string>> FindUnresolvedAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Broadside-PackageCheck");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            urls.Distinct(StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (url, token) =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(url));
                    using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        failures.Add($"{url}: HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    failures.Add($"{url}: {ex.Message}");
                }
            }).ConfigureAwait(false);
        return failures.Order(StringComparer.Ordinal).ToList();
    }
}

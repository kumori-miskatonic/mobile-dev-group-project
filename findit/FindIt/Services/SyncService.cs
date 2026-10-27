using System.Net.Http.Json;
using FindIt.Models;

namespace FindIt.Services;

public sealed class SyncService(HttpClient httpClient)
{
    public async Task<PushChangesResponse> PushAsync(
        string serverBaseUrl,
        IReadOnlyCollection<PendingItemChange> changes,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = serverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS server address.");
        }

        using var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/sync/push",
            new PushChangesRequest { Changes = [.. changes] },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty response.");
    }
}

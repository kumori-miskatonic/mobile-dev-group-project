using System.Net.Http.Json;
using FindIt.Models;

namespace FindIt.Services;

public sealed class SyncService(HttpClient httpClient)
{
    private const string DeviceIdPreference = "findit-device-id";

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
            new PushChangesRequest
            {
                DeviceId = GetOrCreateDeviceId(),
                Changes = [.. changes]
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty response.");
    }

    public async Task<PullChangesResponse> PullAsync(
        string serverBaseUrl,
        DateTime lastPulledAt,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ValidateServerBaseUrl(serverBaseUrl);

        using var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/sync/pull",
            new PullChangesRequest
            {
                DeviceId = GetOrCreateDeviceId(),
                LastPulledAt = lastPulledAt
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PullChangesResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty pull response.");
    }

    public async Task AcknowledgePullAsync(
        string serverBaseUrl,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ValidateServerBaseUrl(serverBaseUrl);

        using var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/sync/pull/ack",
            new AcknowledgePullRequest
            {
                BatchId = batchId,
                DeviceId = GetOrCreateDeviceId()
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<UploadAuditLogsResponse> UploadAuditLogsAsync(
        string serverBaseUrl,
        IReadOnlyCollection<SyncAuditLog> logs,
        CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
            return new UploadAuditLogsResponse();

        var baseUrl = ValidateServerBaseUrl(serverBaseUrl);
        using var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/sync/audit",
            new UploadAuditLogsRequest
            {
                DeviceId = GetOrCreateDeviceId(),
                Logs = logs.Select(log => new SyncAuditLogDto
                {
                    Id = log.Id,
                    Operation = log.Operation,
                    Outcome = log.Outcome,
                    Details = log.Details,
                    ItemId = log.ItemId,
                    OccurredAt = log.OccurredAt
                }).ToList()
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UploadAuditLogsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty audit response.");
    }

    public static string GetDeviceId() => GetOrCreateDeviceId();

    private static string ValidateServerBaseUrl(string serverBaseUrl)
    {
        var baseUrl = serverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS server address.");
        }

        return baseUrl;
    }

    private static string GetOrCreateDeviceId()
    {
        var deviceId = Preferences.Default.Get(DeviceIdPreference, string.Empty);
        if (!string.IsNullOrWhiteSpace(deviceId))
            return deviceId;

        deviceId = Guid.NewGuid().ToString();
        Preferences.Default.Set(DeviceIdPreference, deviceId);
        return deviceId;
    }
}

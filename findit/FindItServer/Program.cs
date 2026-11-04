using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Path.Combine(app.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(dataDirectory, "findit-server.db3");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath
}.ToString();

await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS LostItems (
            Id TEXT PRIMARY KEY,
            ItemName TEXT NOT NULL,
            Description TEXT NOT NULL,
            LocationFound TEXT NOT NULL,
            DateFound TEXT NOT NULL,
            Status TEXT NOT NULL,
            LastModified TEXT NOT NULL,
            ModifiedBy TEXT NOT NULL,
            IsDeleted INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ReceivedChanges (
            ChangeId TEXT PRIMARY KEY,
            LostItemId TEXT NOT NULL,
            ChangeType TEXT NOT NULL,
            ChangedAt TEXT NOT NULL,
            ModifiedBy TEXT NOT NULL,
            ReceivedAt TEXT NOT NULL,
            ProcessedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS DownloadBatches (
            Id TEXT PRIMARY KEY,
            DeviceId TEXT NOT NULL,
            RequestedAfter TEXT NOT NULL,
            ServerWatermark TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            CompletedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS DownloadedChanges (
            BatchId TEXT NOT NULL,
            LostItemId TEXT NOT NULL,
            LastModified TEXT NOT NULL,
            IsDeleted INTEGER NOT NULL,
            PRIMARY KEY (BatchId, LostItemId)
        );
        """;

    await command.ExecuteNonQueryAsync();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "FindItServer",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapPost("/sync/push", async (PushChangesRequest request) =>
{
    if (request.Changes.Count == 0)
        return Results.Ok(new PushChangesResponse([], 0));

    if (request.Changes.Any(change =>
        string.IsNullOrWhiteSpace(change.ChangeId)
        || string.IsNullOrWhiteSpace(change.LostItemId)
        || string.IsNullOrWhiteSpace(change.ChangeType)))
    {
        return Results.BadRequest(new { error = "Every change requires a change ID, item ID, and change type." });
    }

    var acceptedIds = new List<string>();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = connection.BeginTransaction();

    try
    {
        foreach (var change in request.Changes)
        {
            var receiveCommand = connection.CreateCommand();
            receiveCommand.Transaction = transaction;
            receiveCommand.CommandText =
                """
                INSERT OR IGNORE INTO ReceivedChanges
                    (ChangeId, LostItemId, ChangeType, ChangedAt, ModifiedBy, ReceivedAt, ProcessedAt)
                VALUES
                    ($changeId, $lostItemId, $changeType, $changedAt, $modifiedBy, $receivedAt, NULL);
                """;
            receiveCommand.Parameters.AddWithValue("$changeId", change.ChangeId);
            receiveCommand.Parameters.AddWithValue("$lostItemId", change.LostItemId);
            receiveCommand.Parameters.AddWithValue("$changeType", change.ChangeType);
            receiveCommand.Parameters.AddWithValue("$changedAt", ToDatabaseDate(change.ChangedAt));
            receiveCommand.Parameters.AddWithValue("$modifiedBy", change.ModifiedBy);
            receiveCommand.Parameters.AddWithValue("$receivedAt", ToDatabaseDate(DateTime.UtcNow));
            var inserted = await receiveCommand.ExecuteNonQueryAsync();

            if (inserted > 0)
            {
                var itemCommand = connection.CreateCommand();
                itemCommand.Transaction = transaction;
                itemCommand.CommandText =
                    """
                    INSERT INTO LostItems
                        (Id, ItemName, Description, LocationFound, DateFound, Status, LastModified, ModifiedBy, IsDeleted)
                    VALUES
                        ($id, $itemName, $description, $locationFound, $dateFound, $status, $lastModified, $modifiedBy, $isDeleted)
                    ON CONFLICT(Id) DO UPDATE SET
                        ItemName = excluded.ItemName,
                        Description = excluded.Description,
                        LocationFound = excluded.LocationFound,
                        DateFound = excluded.DateFound,
                        Status = excluded.Status,
                        LastModified = excluded.LastModified,
                        ModifiedBy = excluded.ModifiedBy,
                        IsDeleted = excluded.IsDeleted
                    WHERE excluded.LastModified >= LostItems.LastModified;
                    """;
                itemCommand.Parameters.AddWithValue("$id", change.LostItemId);
                itemCommand.Parameters.AddWithValue("$itemName", change.ItemName);
                itemCommand.Parameters.AddWithValue("$description", change.Description);
                itemCommand.Parameters.AddWithValue("$locationFound", change.LocationFound);
                itemCommand.Parameters.AddWithValue("$dateFound", ToDatabaseDate(change.DateFound));
                itemCommand.Parameters.AddWithValue("$status", change.Status);
                itemCommand.Parameters.AddWithValue("$lastModified", ToDatabaseDate(change.LastModified));
                itemCommand.Parameters.AddWithValue("$modifiedBy", change.ModifiedBy);
                itemCommand.Parameters.AddWithValue("$isDeleted", change.IsDeleted ? 1 : 0);
                await itemCommand.ExecuteNonQueryAsync();

                var processedCommand = connection.CreateCommand();
                processedCommand.Transaction = transaction;
                processedCommand.CommandText =
                    "UPDATE ReceivedChanges SET ProcessedAt = $processedAt WHERE ChangeId = $changeId;";
                processedCommand.Parameters.AddWithValue("$processedAt", ToDatabaseDate(DateTime.UtcNow));
                processedCommand.Parameters.AddWithValue("$changeId", change.ChangeId);
                await processedCommand.ExecuteNonQueryAsync();
            }

            acceptedIds.Add(change.ChangeId);
        }

        await transaction.CommitAsync();
        return Results.Ok(new PushChangesResponse(acceptedIds, request.Changes.Count));
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});

app.MapPost("/sync/pull", async (PullChangesRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId))
        return Results.BadRequest(new { error = "A device ID is required." });

    var batchId = Guid.NewGuid().ToString();
    var serverWatermark = DateTime.UtcNow;
    var items = new List<PulledLostItem>();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = connection.BeginTransaction();

    try
    {
        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Id, ItemName, Description, LocationFound, DateFound, Status,
                   LastModified, ModifiedBy, IsDeleted
            FROM LostItems
            WHERE LastModified > $lastPulledAt
              AND LastModified <= $serverWatermark
            ORDER BY LastModified, Id;
            """;
        selectCommand.Parameters.AddWithValue("$lastPulledAt", ToDatabaseDate(request.LastPulledAt));
        selectCommand.Parameters.AddWithValue("$serverWatermark", ToDatabaseDate(serverWatermark));

        await using (var reader = await selectCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                items.Add(new PulledLostItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    ParseDatabaseDate(reader.GetString(4)),
                    reader.GetString(5),
                    ParseDatabaseDate(reader.GetString(6)),
                    reader.GetString(7),
                    reader.GetInt64(8) != 0));
            }
        }

        var batchCommand = connection.CreateCommand();
        batchCommand.Transaction = transaction;
        batchCommand.CommandText =
            """
            INSERT INTO DownloadBatches
                (Id, DeviceId, RequestedAfter, ServerWatermark, CreatedAt, CompletedAt)
            VALUES
                ($id, $deviceId, $requestedAfter, $serverWatermark, $createdAt, NULL);
            """;
        batchCommand.Parameters.AddWithValue("$id", batchId);
        batchCommand.Parameters.AddWithValue("$deviceId", request.DeviceId);
        batchCommand.Parameters.AddWithValue("$requestedAfter", ToDatabaseDate(request.LastPulledAt));
        batchCommand.Parameters.AddWithValue("$serverWatermark", ToDatabaseDate(serverWatermark));
        batchCommand.Parameters.AddWithValue("$createdAt", ToDatabaseDate(DateTime.UtcNow));
        await batchCommand.ExecuteNonQueryAsync();

        foreach (var item in items)
        {
            var detailCommand = connection.CreateCommand();
            detailCommand.Transaction = transaction;
            detailCommand.CommandText =
                """
                INSERT INTO DownloadedChanges (BatchId, LostItemId, LastModified, IsDeleted)
                VALUES ($batchId, $lostItemId, $lastModified, $isDeleted);
                """;
            detailCommand.Parameters.AddWithValue("$batchId", batchId);
            detailCommand.Parameters.AddWithValue("$lostItemId", item.Id);
            detailCommand.Parameters.AddWithValue("$lastModified", ToDatabaseDate(item.LastModified));
            detailCommand.Parameters.AddWithValue("$isDeleted", item.IsDeleted ? 1 : 0);
            await detailCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new PullChangesResponse(batchId, serverWatermark, items));
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});

app.MapPost("/sync/pull/ack", async (AcknowledgePullRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.BatchId) || string.IsNullOrWhiteSpace(request.DeviceId))
        return Results.BadRequest(new { error = "A batch ID and device ID are required." });

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE DownloadBatches
        SET CompletedAt = COALESCE(CompletedAt, $completedAt)
        WHERE Id = $batchId AND DeviceId = $deviceId;
        """;
    command.Parameters.AddWithValue("$completedAt", ToDatabaseDate(DateTime.UtcNow));
    command.Parameters.AddWithValue("$batchId", request.BatchId);
    command.Parameters.AddWithValue("$deviceId", request.DeviceId);

    return await command.ExecuteNonQueryAsync() == 1
        ? Results.Ok(new { status = "acknowledged" })
        : Results.NotFound(new { error = "The download batch was not found." });
});

app.Run();

static string ToDatabaseDate(DateTime value) => value.ToUniversalTime().ToString("O");
static DateTime ParseDatabaseDate(string value) =>
    DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

public sealed record PushChangesRequest(List<PendingItemChange> Changes);

public sealed record PendingItemChange(
    string ChangeId,
    string LostItemId,
    string ChangeType,
    DateTime ChangedAt,
    string ItemName,
    string Description,
    string LocationFound,
    DateTime DateFound,
    string Status,
    DateTime LastModified,
    string ModifiedBy,
    bool IsDeleted);

public sealed record PushChangesResponse(List<string> AcceptedChangeIds, int ReceivedCount);

public sealed record PullChangesRequest(string DeviceId, DateTime LastPulledAt);

public sealed record PullChangesResponse(
    string BatchId,
    DateTime ServerWatermark,
    List<PulledLostItem> Items);

public sealed record PulledLostItem(
    string Id,
    string ItemName,
    string Description,
    string LocationFound,
    DateTime DateFound,
    string Status,
    DateTime LastModified,
    string ModifiedBy,
    bool IsDeleted);

public sealed record AcknowledgePullRequest(string BatchId, string DeviceId);

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

app.Run();

static string ToDatabaseDate(DateTime value) => value.ToUniversalTime().ToString("O");

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

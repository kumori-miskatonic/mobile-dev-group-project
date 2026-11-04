using SQLite;
using FindIt.Models;

namespace FindIt.Data;

public class FindItDatabase
{
    private const string PullCursorKey = "PullCursor";
    private const string PendingPullBatchKey = "PendingPullBatch";
    private const string PendingPullWatermarkKey = "PendingPullWatermark";
    private readonly SQLiteAsyncConnection _database;
    private Task? _initializationTask;

    public FindItDatabase()
    {
        string dbPath = Path.Combine(
            FileSystem.AppDataDirectory,
            "findit.db3"
        );

        _database = new SQLiteAsyncConnection(dbPath);
    }

    public Task InitializeAsync()
    {
        return _initializationTask ??= InitializeDatabaseAsync();
    }

    public async Task<List<LostItem>> GetItemsAsync()
    {
        await InitializeAsync();

        return await _database.Table<LostItem>()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.DateFound)
            .ToListAsync();
    }

    public async Task<List<ItemChange>> GetChangesAsync()
    {
        await InitializeAsync();

        return await _database.Table<ItemChange>()
            .OrderByDescending(change => change.ChangedAt)
            .ToListAsync();
    }

    public async Task<List<PendingItemChange>> GetPendingChangesAsync()
    {
        await InitializeAsync();

        var changes = await _database.Table<ItemChange>()
            .OrderBy(change => change.ChangedAt)
            .ToListAsync();
        var items = await _database.Table<LostItem>().ToListAsync();
        var itemsById = items.ToDictionary(item => item.Id);

        return changes.Select(change =>
        {
            itemsById.TryGetValue(change.LostItemId, out var item);

            return new PendingItemChange
            {
                ChangeId = change.Id,
                LostItemId = change.LostItemId,
                ChangeType = change.ChangeType,
                ChangedAt = change.ChangedAt,
                ItemName = ValueOrFallback(change.ItemName, item?.ItemName),
                Description = ValueOrFallback(change.Description, item?.Description),
                LocationFound = ValueOrFallback(change.LocationFound, item?.LocationFound),
                DateFound = change.DateFound == default ? item?.DateFound ?? default : change.DateFound,
                Status = ValueOrFallback(change.Status, item?.Status),
                LastModified = change.ChangedAt,
                ModifiedBy = ValueOrFallback(change.ModifiedBy, item?.ModifiedBy),
                IsDeleted = change.ChangeType == "Deleted" || change.IsDeleted
            };
        }).ToList();
    }

    public async Task ClearPendingChangesAsync(IEnumerable<string> acceptedChangeIds)
    {
        await InitializeAsync();

        var ids = acceptedChangeIds.Distinct().ToList();
        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var id in ids)
                connection.Delete<ItemChange>(id);
        });
    }

    public async Task<DateTime> GetLastPullCursorAsync()
    {
        await InitializeAsync();

        var state = await _database.FindAsync<SyncState>(PullCursorKey);
        return state is not null
            && DateTime.TryParse(state.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor)
                ? cursor.ToUniversalTime()
                : DateTime.UnixEpoch;
    }

    public async Task<int> StagePulledChangesAsync(PullChangesResponse response)
    {
        await InitializeAsync();

        var items = response.Items.ToList();
        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var incoming in items)
            {
                connection.InsertOrReplace(new IncomingChange
                {
                    Id = $"{response.BatchId}:{incoming.Id}",
                    BatchId = response.BatchId,
                    LostItemId = incoming.Id,
                    ItemName = incoming.ItemName,
                    Description = incoming.Description,
                    LocationFound = incoming.LocationFound,
                    DateFound = incoming.DateFound,
                    Status = incoming.Status,
                    LastModified = incoming.LastModified.ToUniversalTime(),
                    ModifiedBy = incoming.ModifiedBy,
                    IsDeleted = incoming.IsDeleted,
                    DownloadedAt = DateTime.UtcNow,
                    ProcessedAt = null
                });
            }

            connection.InsertOrReplace(new SyncState
            {
                Key = PendingPullBatchKey,
                Value = response.BatchId
            });
            connection.InsertOrReplace(new SyncState
            {
                Key = PendingPullWatermarkKey,
                Value = response.ServerWatermark.ToUniversalTime().ToString("O")
            });
        });

        return items.Count;
    }

    public async Task<int> GetUnprocessedIncomingCountAsync()
    {
        await InitializeAsync();
        return await _database.Table<IncomingChange>()
            .Where(change => change.ProcessedAt == null)
            .CountAsync();
    }

    public async Task<PullApplyResult> ApplyStagedChangesAsync()
    {
        await InitializeAsync();

        var batchState = await _database.FindAsync<SyncState>(PendingPullBatchKey)
            ?? throw new InvalidOperationException("Download server changes before processing them.");
        var watermarkState = await _database.FindAsync<SyncState>(PendingPullWatermarkKey)
            ?? throw new InvalidOperationException("The downloaded batch is missing its server timestamp.");
        if (!DateTime.TryParse(
            watermarkState.Value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var serverWatermark))
        {
            throw new InvalidOperationException("The downloaded batch has an invalid server timestamp.");
        }

        var staged = await _database.Table<IncomingChange>()
            .Where(change => change.BatchId == batchState.Value && change.ProcessedAt == null)
            .ToListAsync();
        var result = new PullApplyResult { BatchId = batchState.Value };
        var processedAt = DateTime.UtcNow;

        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var incoming in staged)
            {
                var existing = connection.Find<LostItem>(incoming.LostItemId);
                if (existing is not null
                    && existing.LastModified.ToUniversalTime() > incoming.LastModified.ToUniversalTime())
                {
                    result.Ignored++;
                    incoming.ProcessedAt = processedAt;
                    connection.Update(incoming);
                    continue;
                }

                var item = existing ?? new LostItem { Id = incoming.LostItemId };
                item.ItemName = incoming.ItemName;
                item.Description = incoming.Description;
                item.LocationFound = incoming.LocationFound;
                item.DateFound = incoming.DateFound;
                item.Status = incoming.Status;
                item.LastModified = incoming.LastModified.ToUniversalTime();
                item.ModifiedBy = incoming.ModifiedBy;
                item.IsDeleted = incoming.IsDeleted;

                if (existing is null)
                {
                    connection.Insert(item);
                    if (incoming.IsDeleted)
                        result.Deleted++;
                    else
                        result.Inserted++;
                }
                else
                {
                    connection.Update(item);
                    if (incoming.IsDeleted)
                        result.Deleted++;
                    else
                        result.Updated++;
                }

                incoming.ProcessedAt = processedAt;
                connection.Update(incoming);
            }

            connection.InsertOrReplace(new SyncState
            {
                Key = PullCursorKey,
                Value = serverWatermark.ToUniversalTime().ToString("O")
            });
        });

        return result;
    }

    public async Task CreateItemAsync(LostItem item)
    {
        await InitializeAsync();

        item.Id = string.IsNullOrWhiteSpace(item.Id)
            ? Guid.NewGuid().ToString()
            : item.Id;
        item.LastModified = DateTime.UtcNow;
        item.IsDeleted = false;

        var change = CreateChange(item, "Created");

        await _database.RunInTransactionAsync(connection =>
        {
            connection.Insert(item);
            connection.Insert(change);
        });
    }

    public async Task UpdateItemAsync(LostItem item)
    {
        await InitializeAsync();

        item.LastModified = DateTime.UtcNow;
        var change = CreateChange(item, "Updated");

        await _database.RunInTransactionAsync(connection =>
        {
            connection.Update(item);
            connection.Insert(change);
        });
    }

    public async Task DeleteItemAsync(LostItem item)
    {
        await InitializeAsync();

        item.IsDeleted = true;
        item.LastModified = DateTime.UtcNow;
        var change = CreateChange(item, "Deleted");

        await _database.RunInTransactionAsync(connection =>
        {
            connection.Update(item);
            connection.Insert(change);
        });
    }

    private async Task InitializeDatabaseAsync()
    {
        await _database.CreateTableAsync<LostItem>();
        await _database.CreateTableAsync<ItemChange>();
        await _database.CreateTableAsync<SyncState>();
        await _database.CreateTableAsync<IncomingChange>();
    }

    private static ItemChange CreateChange(LostItem item, string changeType)
    {
        return new ItemChange
        {
            LostItemId = item.Id,
            ChangeType = changeType,
            ChangedAt = item.LastModified,
            ModifiedBy = item.ModifiedBy,
            ItemName = item.ItemName,
            Description = item.Description,
            LocationFound = item.LocationFound,
            DateFound = item.DateFound,
            Status = item.Status,
            IsDeleted = item.IsDeleted,
            Summary = $"{item.ItemName} | {item.Status} | {item.LocationFound}"
        };
    }

    private static string ValueOrFallback(string value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
}

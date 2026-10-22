using SQLite;
using FindIt.Models;

namespace FindIt.Data;

public class FindItDatabase
{
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
            Summary = $"{item.ItemName} | {item.Status} | {item.LocationFound}"
        };
    }
}

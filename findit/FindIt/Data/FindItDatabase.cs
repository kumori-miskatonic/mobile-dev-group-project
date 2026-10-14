using SQLite;
using FindIt.Models;

namespace FindIt.Data;

public class FindItDatabase
{
    private readonly SQLiteAsyncConnection _database;

    public FindItDatabase()
    {
        string dbPath = Path.Combine(
            FileSystem.AppDataDirectory,
            "findit.db3"
        );

        _database = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        await _database.CreateTableAsync<LostItem>();
    }
}

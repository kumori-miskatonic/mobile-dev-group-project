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
        """;

    await command.ExecuteNonQueryAsync();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "FindItServer",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();

using SQLite;

namespace FindIt.Models;

[Table("SyncState")]
public sealed class SyncState
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

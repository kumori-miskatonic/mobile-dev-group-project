using SQLite;

namespace FindIt.Models;

[Table("SyncAuditLogs")]
public sealed class SyncAuditLog
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool IsSynced { get; set; }
}

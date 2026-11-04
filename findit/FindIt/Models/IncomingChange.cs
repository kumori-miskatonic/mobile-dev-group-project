using SQLite;

namespace FindIt.Models;

[Table("IncomingChanges")]
public sealed class IncomingChange
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;
    [Indexed]
    public string BatchId { get; set; } = string.Empty;
    [Indexed]
    public string LostItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationFound { get; set; } = string.Empty;
    public DateTime DateFound { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime DownloadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

using SQLite;

namespace FindIt.Models;

[Table("ItemChanges")]
public class ItemChange
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string LostItemId { get; set; } = string.Empty;

    public string ChangeType { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationFound { get; set; } = string.Empty;
    public DateTime DateFound { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public string Summary { get; set; } = string.Empty;
}

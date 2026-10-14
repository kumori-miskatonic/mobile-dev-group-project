using SQLite;

namespace FindIt.Models;

public class LostItem
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ItemName { get; set; } = "";
    public string Description { get; set; } = "";
    public string LocationFound { get; set; } = "";

    public DateTime DateFound { get; set; }
    public string Status { get; set; } = "Unclaimed";

    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = "";

    public bool IsDeleted { get; set; } = false;
}

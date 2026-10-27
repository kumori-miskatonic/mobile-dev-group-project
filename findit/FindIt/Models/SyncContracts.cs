namespace FindIt.Models;

public sealed class PushChangesRequest
{
    public List<PendingItemChange> Changes { get; set; } = [];
}

public sealed class PendingItemChange
{
    public string ChangeId { get; set; } = string.Empty;
    public string LostItemId { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationFound { get; set; } = string.Empty;
    public DateTime DateFound { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public sealed class PushChangesResponse
{
    public List<string> AcceptedChangeIds { get; set; } = [];
    public int ReceivedCount { get; set; }
}

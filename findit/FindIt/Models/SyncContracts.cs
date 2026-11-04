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

public sealed class PullChangesRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime LastPulledAt { get; set; }
}

public sealed class PullChangesResponse
{
    public string BatchId { get; set; } = string.Empty;
    public DateTime ServerWatermark { get; set; }
    public List<PulledLostItem> Items { get; set; } = [];
}

public sealed class PulledLostItem
{
    public string Id { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationFound { get; set; } = string.Empty;
    public DateTime DateFound { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public sealed class AcknowledgePullRequest
{
    public string BatchId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class PullApplyResult
{
    public string BatchId { get; set; } = string.Empty;
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Ignored { get; set; }
}

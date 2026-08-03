namespace Aegis.Domain.WorkOrders;

/// <summary>Where a work order sits in its execution lifecycle.</summary>
public enum WorkOrderStatus
{
    /// <summary>Created but not yet assigned to anyone.</summary>
    Draft = 0,

    /// <summary>Assigned to a technician, awaiting the scheduled or next start.</summary>
    Scheduled = 1,

    /// <summary>A technician is actively working it.</summary>
    InProgress = 2,

    /// <summary>The work is done.</summary>
    Completed = 3,

    /// <summary>Withdrawn without being completed.</summary>
    Cancelled = 4,
}

/// <summary>
/// How urgently the work needs doing.
/// </summary>
/// <remarks>
/// A separate scale from <c>IncidentSeverity</c> and <c>AssetCriticality</c>, deliberately. A
/// Critical incident does not automatically produce a Critical work order — dispatch is a
/// planning decision informed by severity, not a mechanical copy of it, and collapsing the three
/// scales into one shared enum would remove the dispatcher's judgement from that decision.
/// </remarks>
public enum WorkOrderPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

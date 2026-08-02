namespace Aegis.Domain.Assets;

/// <summary>
/// The kind of infrastructure an asset represents.
/// </summary>
/// <remarks>
/// A deliberately flat, cross-sector list rather than a per-sector hierarchy. A municipality
/// operating water, roads and lighting needs all of these in one registry, and the alternative —
/// a type hierarchy per sector — turns "show me everything overdue for inspection" into a union
/// across tables. Sector-specific attributes belong in the metadata bag, not in the type.
/// </remarks>
public enum AssetType
{
    /// <summary>A length of pipe in a water or wastewater network.</summary>
    Pipe = 0,

    /// <summary>A pump, at a station or inline.</summary>
    Pump = 1,

    /// <summary>An isolation, control or pressure-reducing valve.</summary>
    Valve = 2,

    /// <summary>A fire hydrant.</summary>
    Hydrant = 3,

    /// <summary>A storage or treatment tank.</summary>
    Tank = 4,

    /// <summary>A treatment works or plant.</summary>
    TreatmentPlant = 5,

    /// <summary>An electrical transformer.</summary>
    Transformer = 6,

    /// <summary>An electrical substation.</summary>
    Substation = 7,

    /// <summary>A section of overhead or underground line.</summary>
    PowerLine = 8,

    /// <summary>A street light column.</summary>
    StreetLight = 9,

    /// <summary>A carriageway section.</summary>
    Road = 10,

    /// <summary>A bridge or culvert.</summary>
    Bridge = 11,

    /// <summary>A drainage gully or manhole.</summary>
    Drain = 12,

    /// <summary>A telemetry or monitoring device.</summary>
    Sensor = 13,

    /// <summary>A site containing other assets, such as a pumping station.</summary>
    Site = 14,

    /// <summary>Anything not covered above.</summary>
    Other = 99,
}

/// <summary>Where an asset sits in its operational lifecycle.</summary>
public enum AssetStatus
{
    /// <summary>Approved but not yet built or installed.</summary>
    Planned = 0,

    /// <summary>Installed and in service.</summary>
    Operational = 1,

    /// <summary>Temporarily out of service for planned work.</summary>
    UnderMaintenance = 2,

    /// <summary>Out of service and not currently usable.</summary>
    Faulted = 3,

    /// <summary>Permanently retired. Retained for history, never deleted.</summary>
    Decommissioned = 4,
}

/// <summary>
/// How much it matters if this asset fails.
/// </summary>
/// <remarks>
/// Consequence of failure, not probability. A thirty-year-old valve on a spur serving four houses
/// is far more likely to fail than the main feeding a hospital, and far less important. Conflating
/// the two is how maintenance budgets end up optimising the wrong thing; probability is tracked
/// separately as condition.
/// </remarks>
public enum AssetCriticality
{
    /// <summary>Failure is an inconvenience.</summary>
    Low = 0,

    /// <summary>Failure disrupts a small number of customers.</summary>
    Medium = 1,

    /// <summary>Failure disrupts many customers or a key route.</summary>
    High = 2,

    /// <summary>Failure endangers life, or takes out a hospital, school or major artery.</summary>
    Critical = 3,
}

/// <summary>
/// Assessed physical condition, on the 1-to-5 scale used across UK and EU asset management practice.
/// </summary>
/// <remarks>
/// Five graded values rather than a free-form percentage. Inspectors record what they can actually
/// judge from a visual survey, and a scale that invites "63%" produces numbers with false
/// precision that later analysis treats as real.
/// </remarks>
public enum AssetCondition
{
    /// <summary>Not yet assessed.</summary>
    Unknown = 0,

    /// <summary>As new. No visible deterioration.</summary>
    VeryGood = 1,

    /// <summary>Minor deterioration, no action needed.</summary>
    Good = 2,

    /// <summary>Moderate deterioration. Monitor.</summary>
    Fair = 3,

    /// <summary>Significant deterioration. Intervention needed.</summary>
    Poor = 4,

    /// <summary>Failed or on the point of failure. Immediate action.</summary>
    VeryPoor = 5,
}

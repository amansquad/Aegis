namespace Aegis.Domain.Incidents;

/// <summary>The kind of problem being reported.</summary>
/// <remarks>
/// Lives in the domain, not beside the AI port where it started. These are categories the business
/// triages and reports on; that an extractor happens to classify into them is incidental. Keeping
/// them in the Application layer would also have been impossible once the <c>Incident</c> aggregate
/// needed them, since Aegis.Domain cannot reference Aegis.Application.
/// </remarks>
public enum IncidentCategory
{
    /// <summary>Water escaping from a main, service pipe or fitting.</summary>
    Leak = 0,

    /// <summary>Complete loss of supply.</summary>
    SupplyLoss = 1,

    /// <summary>Discoloured, smelly or otherwise suspect water.</summary>
    WaterQuality = 2,

    /// <summary>Pressure outside acceptable limits.</summary>
    PressureProblem = 3,

    /// <summary>Blockage or flooding in the drainage network.</summary>
    Blockage = 4,

    /// <summary>Physical damage to infrastructure, including third-party strikes.</summary>
    StructuralDamage = 5,

    /// <summary>Loss of power or electrical fault.</summary>
    PowerFault = 6,

    /// <summary>Road surface, signage or street furniture defect.</summary>
    RoadDefect = 7,

    /// <summary>Anything the categories above do not cover.</summary>
    Other = 99,
}

/// <summary>How urgently a reported problem needs attention.</summary>
/// <remarks>
/// Consequence, not the reporter's tone. An angry report about a dripping tap is Low; a calm report
/// about water rising in a basement is High. Conflating urgency with how forcefully something was
/// said is how a queue ends up ordered by who complains loudest.
/// </remarks>
public enum IncidentSeverity
{
    /// <summary>Cosmetic or non-urgent. Schedule with routine work.</summary>
    Low = 0,

    /// <summary>Service affected for a small number of people.</summary>
    Moderate = 1,

    /// <summary>Widespread disruption, or damage that will worsen quickly.</summary>
    High = 2,

    /// <summary>Danger to life or property. Dispatch now.</summary>
    Critical = 3,
}

/// <summary>Where an incident sits in its handling lifecycle.</summary>
public enum IncidentStatus
{
    /// <summary>Received, not yet reviewed by a person.</summary>
    Reported = 0,

    /// <summary>Reviewed and confirmed by a dispatcher.</summary>
    Triaged = 1,

    /// <summary>Work is underway.</summary>
    InProgress = 2,

    /// <summary>The underlying problem has been fixed.</summary>
    Resolved = 3,

    /// <summary>Closed after resolution, or closed without action.</summary>
    Closed = 4,

    /// <summary>The same problem as an incident already on the system.</summary>
    Duplicate = 5,

    /// <summary>Not a genuine problem, or not this organization's responsibility.</summary>
    Rejected = 6,
}

/// <summary>How an incident's structured fields were arrived at.</summary>
public enum ClassificationMethod
{
    /// <summary>A person filled in the details directly.</summary>
    Manual = 0,

    /// <summary>A language model proposed them.</summary>
    Model = 1,

    /// <summary>The rule-based fallback proposed them.</summary>
    Heuristic = 2,
}

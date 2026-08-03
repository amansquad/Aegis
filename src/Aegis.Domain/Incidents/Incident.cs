using Aegis.Domain.Abstractions;
using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents.Events;

namespace Aegis.Domain.Incidents;

/// <summary>
/// A reported problem with the organization's infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reporter's words are never overwritten.</b> <see cref="ReportText"/> holds exactly what
/// was submitted, and every structured field lives beside it as an interpretation. When a
/// dispatcher corrects a classification, the original stays — because the question "what did they
/// actually say?" is asked constantly during an investigation, and an interface that has quietly
/// replaced the answer with a machine's paraphrase cannot answer it.
/// </para>
/// <para>
/// <b>Classification is proposed, then confirmed.</b> The extractor's output is stored with its
/// provenance and confidence; triage records what a human decided. Keeping both is what makes it
/// possible to say later whether the model is any good, which is not a question that can be
/// answered retrospectively if only the final value was kept.
/// </para>
/// </remarks>
public sealed class Incident : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity, ISoftDeletable
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private Incident()
    {
        Reference = string.Empty;
        ReportText = string.Empty;
        Summary = string.Empty;
    }

    private Incident(
        Guid id,
        Guid organizationId,
        string reference,
        string reportText,
        string summary,
        IncidentCategory category,
        IncidentSeverity severity,
        DateTimeOffset reportedOnUtc) : base(id)
    {
        OrganizationId = organizationId;
        Reference = reference;
        ReportText = reportText;
        Summary = summary;
        Category = category;
        Severity = severity;
        ReportedOnUtc = reportedOnUtc;
        Status = IncidentStatus.Reported;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// The reference quoted to the public and over the radio, such as <c>INC-2026-4F2A91C3</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from the identifier rather than from a per-organization counter. A sequential number
    /// is what operators would prefer, but producing one correctly needs a serialised sequence per
    /// tenant — a write-contention point on the hottest insert path, and a distributed-counter
    /// problem the moment there is more than one instance.
    /// </para>
    /// <para>
    /// The identifier is UUIDv7, so its leading bits are a timestamp: references still sort roughly
    /// in the order incidents arrived, which is most of what people actually want from a sequence.
    /// </para>
    /// </remarks>
    public string Reference { get; private set; }

    /// <summary>The report exactly as submitted. Never modified.</summary>
    public string ReportText { get; private set; }

    /// <summary>An operational one-liner. May be corrected during triage.</summary>
    public string Summary { get; private set; }

    /// <summary>The confirmed category, or the proposed one before triage.</summary>
    public IncidentCategory Category { get; private set; }

    /// <summary>The confirmed severity, or the proposed one before triage.</summary>
    public IncidentSeverity Severity { get; private set; }

    /// <summary>Where the incident sits in its handling lifecycle.</summary>
    public IncidentStatus Status { get; private set; }

    /// <summary>True when the report describes danger to people.</summary>
    public bool PublicSafetyRisk { get; private set; }

    /// <summary>How the structured fields were first arrived at.</summary>
    public ClassificationMethod ClassificationMethod { get; private set; }

    /// <summary>The extractor's confidence, from 0 to 1. Null when entered manually.</summary>
    public double? ClassificationConfidence { get; private set; }

    /// <summary>The category the extractor proposed, retained after any correction.</summary>
    public IncidentCategory? ProposedCategory { get; private set; }

    /// <summary>The severity the extractor proposed, retained after any correction.</summary>
    public IncidentSeverity? ProposedSeverity { get; private set; }

    /// <summary>Location as described in the report, verbatim.</summary>
    public string? LocationHint { get; private set; }

    /// <summary>Where the incident is, once known.</summary>
    public GeoCoordinate? Location { get; private set; }

    /// <summary>The asset this concerns, once resolved.</summary>
    public Guid? AssetId { get; private set; }

    /// <summary>Reporter's name, if given. Optional: anonymous reports are accepted.</summary>
    public string? ReporterName { get; private set; }

    /// <summary>
    /// Reporter's contact details, if given.
    /// </summary>
    /// <remarks>
    /// Personal data belonging to a member of the public, held only so the operator can call back
    /// about this incident. Excluded from the audit trail's value snapshots, because copying it
    /// into a second table would put it somewhere no retention policy is looking.
    /// </remarks>
    public string? ReporterContact { get; private set; }

    /// <summary>When the report was received.</summary>
    public DateTimeOffset ReportedOnUtc { get; private set; }

    /// <summary>When a dispatcher confirmed the classification.</summary>
    public DateTimeOffset? TriagedOnUtc { get; private set; }

    /// <summary>Who confirmed the classification.</summary>
    public Guid? TriagedBy { get; private set; }

    /// <summary>When the underlying problem was fixed.</summary>
    public DateTimeOffset? ResolvedOnUtc { get; private set; }

    /// <summary>Who resolved it.</summary>
    public Guid? ResolvedBy { get; private set; }

    /// <summary>What was done about it.</summary>
    public string? ResolutionNotes { get; private set; }

    /// <summary>The incident this one duplicates, when closed as a duplicate.</summary>
    public Guid? DuplicateOfIncidentId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <summary>True when a human has not yet confirmed the classification.</summary>
    public bool AwaitingTriage => Status == IncidentStatus.Reported;

    /// <summary>True when the incident is still being handled.</summary>
    public bool IsOpen => Status is IncidentStatus.Reported or IncidentStatus.Triaged
        or IncidentStatus.InProgress;

    /// <summary>How long the incident has been open, or took to resolve.</summary>
    public TimeSpan Age(DateTimeOffset now) => (ResolvedOnUtc ?? now) - ReportedOnUtc;

    /// <summary>Records a new report.</summary>
    /// <param name="organizationId">The owning organization.</param>
    /// <param name="reportText">The reporter's own words.</param>
    /// <param name="summary">An operational one-liner.</param>
    /// <param name="category">Proposed or entered category.</param>
    /// <param name="severity">Proposed or entered severity.</param>
    /// <param name="method">How the classification was arrived at.</param>
    /// <param name="confidence">Extractor confidence, or null for manual entry.</param>
    /// <param name="publicSafetyRisk">Whether danger to people was described.</param>
    /// <param name="locationHint">Location as described, verbatim.</param>
    /// <param name="reportedOnUtc">When the report was received.</param>
    public static Result<Incident> Report(
        Guid organizationId,
        string? reportText,
        string? summary,
        IncidentCategory category,
        IncidentSeverity severity,
        ClassificationMethod method,
        double? confidence,
        bool publicSafetyRisk,
        string? locationHint,
        DateTimeOffset reportedOnUtc)
    {
        if (string.IsNullOrWhiteSpace(reportText))
        {
            return Result.Failure<Incident>(Error.Validation(
                "Incident.ReportEmpty",
                "Describe the problem before submitting."));
        }

        var trimmed = reportText.Trim();

        if (trimmed.Length > 8000)
        {
            return Result.Failure<Incident>(Error.Validation(
                "Incident.ReportTooLong",
                "A report cannot exceed 8000 characters."));
        }

        var id = Guid.CreateVersion7();

        var incident = new Incident(
            id,
            organizationId,
            BuildReference(id, reportedOnUtc),
            trimmed,
            string.IsNullOrWhiteSpace(summary) ? Shorten(trimmed) : summary.Trim(),
            category,
            severity,
            reportedOnUtc)
        {
            ClassificationMethod = method,
            ClassificationConfidence = confidence is null ? null : Math.Clamp(confidence.Value, 0, 1),
            PublicSafetyRisk = publicSafetyRisk,
            LocationHint = string.IsNullOrWhiteSpace(locationHint) ? null : locationHint.Trim(),
        };

        // The proposal is recorded separately from the working values, so a later correction does
        // not erase what was originally suggested.
        if (method != ClassificationMethod.Manual)
        {
            incident.ProposedCategory = category;
            incident.ProposedSeverity = severity;
        }

        incident.RaiseDomainEvent(new IncidentReported(
            incident.Id,
            organizationId,
            incident.Reference,
            category,
            severity,
            incident.RequiresReview(),
            publicSafetyRisk));

        return Result.Success(incident);
    }

    /// <summary>True when a human must confirm before this incident is acted on.</summary>
    /// <remarks>
    /// Mirrors the extractor's rule and repeats it here on purpose. The aggregate must be able to
    /// answer this from its own state after being loaded from the database, without the extractor
    /// that produced it still being in the picture.
    /// </remarks>
    public bool RequiresReview() =>
        ClassificationMethod != ClassificationMethod.Manual
        && (PublicSafetyRisk
            || ClassificationMethod == ClassificationMethod.Heuristic
            || ClassificationConfidence is null or < 0.85);

    /// <summary>Attaches the reporter's contact details.</summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> so that an anonymous report is the shorter path rather
    /// than the exceptional one. Anonymity is normal and must not feel like an omission.
    /// </remarks>
    public void RecordReporter(string? name, string? contact)
    {
        ReporterName = Blank(name);
        ReporterContact = Blank(contact);
    }

    /// <summary>Sets the incident's position.</summary>
    public void SetLocation(GeoCoordinate location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    /// <summary>Links the incident to the asset it concerns.</summary>
    /// <remarks>
    /// The asset is supplied by the caller after a tenant-scoped lookup, never taken from the
    /// report. A reporter quoting an asset code produces a hint to resolve, not an identity to
    /// trust.
    /// </remarks>
    public Result LinkToAsset(Guid assetId)
    {
        if (assetId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Incident.InvalidAsset", "An asset id is required."));
        }

        if (AssetId == assetId)
        {
            return Result.Success();
        }

        AssetId = assetId;

        RaiseDomainEvent(new IncidentLinkedToAsset(Id, OrganizationId, assetId));

        return Result.Success();
    }

    /// <summary>Confirms or corrects the classification.</summary>
    public Result Triage(
        IncidentCategory category,
        IncidentSeverity severity,
        string? summary,
        Guid triagedBy,
        DateTimeOffset now)
    {
        if (Status is IncidentStatus.Resolved or IncidentStatus.Closed
            or IncidentStatus.Duplicate or IncidentStatus.Rejected)
        {
            return Result.Failure(Error.Conflict(
                "Incident.NotOpen",
                "This incident has been closed and cannot be triaged."));
        }

        var previousCategory = Category;
        var previousSeverity = Severity;

        Category = category;
        Severity = severity;

        if (!string.IsNullOrWhiteSpace(summary))
        {
            Summary = summary.Trim();
        }

        // Triage makes the classification human-owned. The proposal is retained in
        // ProposedCategory and ProposedSeverity, so the correction remains measurable.
        ClassificationMethod = ClassificationMethod.Manual;
        Status = IncidentStatus.Triaged;
        TriagedOnUtc = now;
        TriagedBy = triagedBy;

        RaiseDomainEvent(new IncidentTriaged(
            Id,
            OrganizationId,
            ProposedCategory ?? previousCategory,
            category,
            ProposedSeverity ?? previousSeverity,
            severity,
            triagedBy));

        return Result.Success();
    }

    /// <summary>Raises the severity of an open incident.</summary>
    public Result Escalate(IncidentSeverity severity, string? reason)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "Incident.NotOpen",
                "Only an open incident can be escalated."));
        }

        if (severity <= Severity)
        {
            return Result.Failure(Error.Validation(
                "Incident.NotAnEscalation",
                "Escalation must raise the severity. Use triage to lower it, which records who " +
                "decided that and when."));
        }

        var previous = Severity;
        Severity = severity;

        RaiseDomainEvent(new IncidentEscalated(Id, OrganizationId, previous, severity, reason?.Trim()));

        return Result.Success();
    }

    /// <summary>Marks work as underway.</summary>
    public Result Start()
    {
        if (Status is not (IncidentStatus.Reported or IncidentStatus.Triaged))
        {
            return Result.Failure(Error.Conflict(
                "Incident.CannotStart",
                "Only a reported or triaged incident can be started."));
        }

        Status = IncidentStatus.InProgress;

        return Result.Success();
    }

    /// <summary>Records that the underlying problem has been fixed.</summary>
    public Result Resolve(string? notes, Guid resolvedBy, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "Incident.NotOpen",
                "This incident is not open."));
        }

        if (now < ReportedOnUtc)
        {
            return Result.Failure(Error.Validation(
                "Incident.ResolvedBeforeReported",
                "An incident cannot be resolved before it was reported."));
        }

        Status = IncidentStatus.Resolved;
        ResolvedOnUtc = now;
        ResolvedBy = resolvedBy;
        ResolutionNotes = Blank(notes);

        RaiseDomainEvent(new IncidentResolved(Id, OrganizationId, resolvedBy, now - ReportedOnUtc));

        return Result.Success();
    }

    /// <summary>Closes the incident as the same problem as another.</summary>
    public Result MarkDuplicateOf(Guid originalIncidentId)
    {
        if (originalIncidentId == Id)
        {
            return Result.Failure(Error.Validation(
                "Incident.SelfDuplicate",
                "An incident cannot duplicate itself."));
        }

        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "Incident.NotOpen",
                "Only an open incident can be marked as a duplicate."));
        }

        Status = IncidentStatus.Duplicate;
        DuplicateOfIncidentId = originalIncidentId;

        RaiseDomainEvent(new IncidentMarkedDuplicate(Id, OrganizationId, originalIncidentId));

        return Result.Success();
    }

    /// <summary>Closes the incident without action.</summary>
    public Result Reject(string? reason)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict("Incident.NotOpen", "This incident is not open."));
        }

        Status = IncidentStatus.Rejected;
        ResolutionNotes = Blank(reason);

        return Result.Success();
    }

    /// <summary>Closes a resolved incident.</summary>
    public Result Close()
    {
        if (Status != IncidentStatus.Resolved)
        {
            return Result.Failure(Error.Conflict(
                "Incident.NotResolved",
                "Only a resolved incident can be closed."));
        }

        Status = IncidentStatus.Closed;

        return Result.Success();
    }

    /// <summary>
    /// Builds the reference from the tail of the identifier, not the head.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first version took the leading eight hex characters and was wrong. UUIDv7's leading 48
    /// bits are a millisecond timestamp, so every incident reported in the same millisecond shared
    /// a reference — and the unique index would have rejected the second one. A unit test creating
    /// two hundred reports in a loop found it immediately; nothing about reading the code would
    /// have.
    /// </para>
    /// <para>
    /// The tail is the random portion, so twelve characters carry roughly 46 bits. A collision
    /// needs on the order of ten million incidents in one organization in one year, which is far
    /// outside what this platform will see, and the unique index remains the actual guarantee.
    /// </para>
    /// <para>
    /// A sequential number would read better and is what operators would ask for. It is rejected
    /// deliberately: producing one correctly needs a database sequence, and a sequence shared
    /// across tenants would let any customer infer the platform's total incident volume from the
    /// gaps in their own references. That is a small leak, but it is the same class of
    /// cross-tenant inference the query filters exist to prevent, and consistency matters more
    /// here than a nicer-looking string.
    /// </para>
    /// </remarks>
    private static string BuildReference(Guid id, DateTimeOffset reportedOnUtc)
    {
        var hex = id.ToString("N");

        return $"INC-{reportedOnUtc:yyyy}-{hex[^12..].ToUpperInvariant()}";
    }

    private static string Shorten(string text) =>
        text.Length <= 160 ? text : $"{text[..157]}…";

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;

namespace Aegis.Domain.Auditing;

/// <summary>The kind of change an audit entry records.</summary>
public enum AuditAction
{
    /// <summary>A row was inserted.</summary>
    Created = 0,

    /// <summary>A row was modified.</summary>
    Updated = 1,

    /// <summary>A row was logically deleted.</summary>
    Deleted = 2,
}

/// <summary>
/// An append-only record of a single change to a single tracked entity.
/// </summary>
/// <remarks>
/// <para>
/// Written automatically by <c>AuditTrailInterceptor</c> from EF Core's change tracker, never by
/// handler code. That placement is deliberate: an audit log a developer must remember to write is
/// an audit log with gaps, and gaps are precisely what an auditor looks for.
/// </para>
/// <para>
/// Entries are immutable once written — there are no setters and no update path. An audit trail
/// that can be edited answers no question worth asking. Retention and archival are handled by a
/// scheduled job against the table, not by mutation.
/// </para>
/// <para>
/// Note that this type implements <see cref="ITenantOwned"/> but neither
/// <see cref="IAuditableEntity"/> nor <see cref="ISoftDeletable"/>. Auditing the audit log is
/// circular, and a deletable audit log defeats its purpose.
/// </para>
/// </remarks>
public sealed class AuditTrailEntry : Entity<Guid>, ITenantOwned
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private AuditTrailEntry()
    {
        EntityName = string.Empty;
        EntityId = string.Empty;
    }

    private AuditTrailEntry(
        Guid organizationId,
        string entityName,
        string entityId,
        AuditAction action,
        DateTimeOffset occurredOnUtc) : base(Guid.CreateVersion7())
    {
        OrganizationId = organizationId;
        EntityName = entityName;
        EntityId = entityId;
        Action = action;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>The CLR type name of the audited entity, for example <c>Asset</c>.</summary>
    public string EntityName { get; private set; }

    /// <summary>The audited entity's primary key, serialised as text to support composite keys.</summary>
    public string EntityId { get; private set; }

    /// <summary>The kind of change recorded.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>UTC instant at which the change was committed.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>The user responsible. Null for system- or migration-driven changes.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// The acting user's email, denormalised at write time.
    /// </summary>
    /// <remarks>
    /// Deliberate denormalisation. Joining to the users table would show the account's
    /// <em>current</em> email, so renaming an account would silently rewrite history. An audit
    /// entry must report who acted under the identity they held at the time.
    /// </remarks>
    public string? UserEmail { get; private set; }

    /// <summary>Names of the columns that changed, comma-delimited. Null for inserts and deletes.</summary>
    public string? ChangedColumns { get; private set; }

    /// <summary>JSON snapshot of the values before the change. Null for inserts.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON snapshot of the values after the change. Null for deletes.</summary>
    public string? NewValues { get; private set; }

    /// <summary>
    /// Correlation identifier tying this entry to the HTTP request that caused it, and to every
    /// Serilog line emitted while handling that request.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Originating IP address, where the change arrived over HTTP.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Originating user agent, where the change arrived over HTTP.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Creates an audit entry. Called only by the persistence interceptor.
    /// </summary>
    /// <param name="organizationId">The owning tenant.</param>
    /// <param name="entityName">CLR type name of the audited entity.</param>
    /// <param name="entityId">Primary key of the audited entity, as text.</param>
    /// <param name="action">The kind of change.</param>
    /// <param name="occurredOnUtc">When the change was committed.</param>
    /// <returns>A new, immutable audit entry.</returns>
    public static AuditTrailEntry Record(
        Guid organizationId,
        string entityName,
        string entityId,
        AuditAction action,
        DateTimeOffset occurredOnUtc)
    {
        DomainException.RequireNotBlank(entityName, "Audit.EntityNameRequired", "Entity name is required.");
        DomainException.RequireNotBlank(entityId, "Audit.EntityIdRequired", "Entity id is required.");

        return new AuditTrailEntry(organizationId, entityName, entityId, action, occurredOnUtc);
    }

    /// <summary>Attaches the acting identity. Called during construction by the interceptor.</summary>
    public AuditTrailEntry WithActor(Guid? userId, string? userEmail)
    {
        UserId = userId;
        UserEmail = userEmail;
        return this;
    }

    /// <summary>Attaches the before/after value snapshots and the list of changed columns.</summary>
    public AuditTrailEntry WithChanges(string? oldValues, string? newValues, string? changedColumns)
    {
        OldValues = oldValues;
        NewValues = newValues;
        ChangedColumns = changedColumns;
        return this;
    }

    /// <summary>Attaches the request context in which the change occurred.</summary>
    public AuditTrailEntry WithRequestContext(string? correlationId, string? ipAddress, string? userAgent)
    {
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        return this;
    }
}

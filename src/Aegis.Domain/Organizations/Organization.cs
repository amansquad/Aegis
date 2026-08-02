using System.Text.RegularExpressions;
using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;

namespace Aegis.Domain.Organizations;

/// <summary>The kind of infrastructure an organization operates.</summary>
public enum OrganizationKind
{
    /// <summary>Water supply and wastewater networks.</summary>
    WaterUtility = 0,

    /// <summary>Electricity transmission or distribution.</summary>
    PowerUtility = 1,

    /// <summary>Roads, bridges and street furniture.</summary>
    RoadAuthority = 2,

    /// <summary>Gas distribution networks.</summary>
    GasUtility = 3,

    /// <summary>A municipality operating several of the above.</summary>
    Municipality = 4,

    /// <summary>Anything not covered by the categories above.</summary>
    Other = 99,
}

/// <summary>Whether an organization may currently use the platform.</summary>
public enum OrganizationStatus
{
    /// <summary>Registered and operating normally.</summary>
    Active = 0,

    /// <summary>Access withdrawn, typically for non-payment. Data is retained.</summary>
    Suspended = 1,
}

/// <summary>
/// A tenant: the utility, authority or municipality that owns a body of infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// The root of the tenancy model. Every <see cref="ITenantOwned"/> row in the database belongs to
/// exactly one of these, and the organization's identifier is what the JWT carries and what every
/// global query filter compares against.
/// </para>
/// <para>
/// Note that it implements <see cref="ITenantRoot"/> rather than <see cref="ITenantOwned"/> — it
/// does not belong to a tenant, it <em>is</em> one. The distinction is not pedantic: applying the
/// wrong filter here would either hide every organization from itself or expose every customer of
/// the platform to every other.
/// </para>
/// </remarks>
public sealed partial class Organization : AggregateRoot<Guid>, ITenantRoot, IAuditableEntity
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private Organization()
    {
        Name = string.Empty;
        Slug = string.Empty;
        TimeZoneId = string.Empty;
    }

    private Organization(Guid id, string name, string slug, OrganizationKind kind, string timeZoneId)
        : base(id)
    {
        Name = name;
        Slug = slug;
        Kind = kind;
        TimeZoneId = timeZoneId;
        Status = OrganizationStatus.Active;
    }

    /// <summary>Display name, as the organization gave it.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// URL-safe identifier, unique across the platform.
    /// </summary>
    /// <remarks>
    /// Exists so that a tenant can be addressed in a URL or a subdomain without exposing its
    /// primary key, and so that support staff can refer to "northern-water" rather than to a GUID.
    /// </remarks>
    public string Slug { get; private set; }

    /// <summary>What kind of infrastructure the organization operates.</summary>
    public OrganizationKind Kind { get; private set; }

    /// <summary>Whether the organization may currently use the platform.</summary>
    public OrganizationStatus Status { get; private set; }

    /// <summary>
    /// IANA or Windows time zone identifier for the organization's operating region.
    /// </summary>
    /// <remarks>
    /// Every timestamp is stored in UTC. This is what turns "maintenance due at 06:00" into the
    /// right instant for a crew that starts its shift at 06:00 local, across daylight-saving
    /// boundaries. Storing local times instead would make every scheduled job ambiguous twice a year.
    /// </remarks>
    public string TimeZoneId { get; private set; }

    /// <summary>Optional contact address for platform-level correspondence.</summary>
    public string? ContactEmail { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <summary>True when the organization is permitted to operate.</summary>
    public bool IsOperational => Status == OrganizationStatus.Active;

    /// <summary>Registers a new organization.</summary>
    /// <param name="name">Display name.</param>
    /// <param name="kind">Kind of infrastructure operated.</param>
    /// <param name="timeZoneId">Time zone identifier for the operating region.</param>
    /// <param name="slug">URL-safe identifier. Derived from the name when omitted.</param>
    public static Result<Organization> Register(
        string? name,
        OrganizationKind kind,
        string timeZoneId,
        string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Organization>(
                Error.Validation("Organization.NameRequired", "An organization name is required."));
        }

        var trimmedName = name.Trim();

        if (trimmedName.Length > 200)
        {
            return Result.Failure<Organization>(
                Error.Validation("Organization.NameTooLong", "The name cannot exceed 200 characters."));
        }

        var resolvedSlug = string.IsNullOrWhiteSpace(slug) ? Slugify(trimmedName) : Slugify(slug);

        if (resolvedSlug.Length == 0)
        {
            return Result.Failure<Organization>(Error.Validation(
                "Organization.SlugInvalid",
                "A URL-safe identifier could not be derived from the name. Supply one explicitly."));
        }

        if (!IsValidTimeZone(timeZoneId))
        {
            return Result.Failure<Organization>(Error.Validation(
                "Organization.TimeZoneInvalid",
                $"'{timeZoneId}' is not a recognised time zone identifier."));
        }

        return Result.Success(new Organization(
            Guid.CreateVersion7(),
            trimmedName,
            resolvedSlug,
            kind,
            timeZoneId));
    }

    /// <summary>Updates the organization's descriptive details.</summary>
    public Result UpdateProfile(string? name, string timeZoneId, string? contactEmail)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(
                Error.Validation("Organization.NameRequired", "An organization name is required."));
        }

        if (!IsValidTimeZone(timeZoneId))
        {
            return Result.Failure(Error.Validation(
                "Organization.TimeZoneInvalid",
                $"'{timeZoneId}' is not a recognised time zone identifier."));
        }

        Name = name.Trim();
        TimeZoneId = timeZoneId;
        ContactEmail = contactEmail?.Trim().ToLowerInvariant();

        return Result.Success();
    }

    /// <summary>Withdraws platform access while retaining all data.</summary>
    public void Suspend() => Status = OrganizationStatus.Suspended;

    /// <summary>Restores platform access.</summary>
    public void Reinstate() => Status = OrganizationStatus.Active;

    /// <summary>Converts a display name into a URL-safe identifier.</summary>
    private static string Slugify(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var collapsed = NonSlugCharacters().Replace(lowered, "-");

        return collapsed.Trim('-');
    }

    /// <summary>
    /// Validates a time zone identifier against the host's database.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeZoneInfo"/> lives in the BCL, so this does not breach the domain's
    /// zero-dependency rule. .NET 8 and later resolve IANA identifiers on Windows as well as Unix,
    /// so "Europe/London" is accepted on both.
    /// </remarks>
    private static bool IsValidTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex NonSlugCharacters();
}

using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Common.Extensions;
using Aegis.Application.Common.Models;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using Aegis.Domain.Identity.ValueObjects;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Identity.Queries;

/// <summary>A user as shown in a management list.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Email">Email address.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DisplayName">Full name for display.</param>
/// <param name="Status">Account lifecycle state.</param>
/// <param name="EmailConfirmed">Whether the address has been confirmed.</param>
/// <param name="LastSignedInOnUtc">When the user last signed in.</param>
/// <param name="CreatedOnUtc">When the account was created.</param>
/// <param name="Roles">Role names held.</param>
/// <remarks>
/// A projection, not the entity. The <c>User</c> aggregate carries a password hash, a security
/// stamp and a refresh token chain, none of which should ever be serialised to a client — and
/// returning the entity directly means one careless change adds them to the payload.
/// </remarks>
public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string DisplayName,
    UserStatus Status,
    bool EmailConfirmed,
    DateTimeOffset? LastSignedInOnUtc,
    DateTimeOffset CreatedOnUtc,
    IReadOnlyCollection<string> Roles);

/// <summary>Lists users in the current organization.</summary>
/// <remarks>
/// Tenant scoping is not written here. The EF Core global query filter applies it, which is exactly
/// the property that makes this safe: the handler reads as though tenancy did not exist and is
/// nonetheless incapable of returning another organization's users. The integration suite asserts
/// it rather than trusting it.
/// </remarks>
public sealed record ListUsersQuery : PaginatedQuery, IQuery<PagedResult<UserListItemDto>>
{
    /// <summary>Restricts results to a single lifecycle state.</summary>
    public UserStatus? Status { get; init; }

    /// <summary>Restricts results to holders of a specific role.</summary>
    public Guid? RoleId { get; init; }

    /// <summary>
    /// Fields this query can be sorted by.
    /// </summary>
    /// <remarks>
    /// Declared explicitly rather than derived from the DTO. Sorting is applied to the entity
    /// before projection — ordering a projected result makes EF compose the sort over the
    /// projection's constructor, which it cannot translate — so the valid fields are those the
    /// entity exposes as scalars. <c>Email</c> and <c>DisplayName</c> are deliberately absent:
    /// the first is a value object behind a converter, the second is computed.
    /// </remarks>
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        nameof(UserListItemDto.Id),
        nameof(UserListItemDto.FirstName),
        nameof(UserListItemDto.LastName),
        nameof(UserListItemDto.Status),
        nameof(UserListItemDto.EmailConfirmed),
        nameof(UserListItemDto.LastSignedInOnUtc),
        nameof(UserListItemDto.CreatedOnUtc),
    };
}

/// <summary>Validates <see cref="ListUsersQuery"/>.</summary>
/// <remarks>
/// An unknown sort field is rejected here rather than silently ignored. A client that misspells
/// <c>createdOn</c> would otherwise receive arbitrarily ordered data and page through it
/// incorrectly, seeing some records twice and others never — a bug that looks like data loss.
/// </remarks>
public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    /// <summary>Initialises the validator.</summary>
    public ListUsersQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(sortBy => sortBy is null || ListUsersQuery.SortableFields.Contains(sortBy))
            .WithMessage(_ =>
                "Unknown sort field. Valid values: " +
                string.Join(", ", ListUsersQuery.SortableFields.OrderBy(p => p, StringComparer.Ordinal)));

        RuleFor(q => q.SearchTerm).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ListUsersQuery"/>.</summary>
internal sealed class ListUsersQueryHandler(IAegisDbContext context)
    : IQueryHandler<ListUsersQuery, PagedResult<UserListItemDto>>
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<UserListItemDto>>> Handle(
        ListUsersQuery request,
        CancellationToken cancellationToken)
    {
        // Role names are resolved from a separate lookup rather than a join. Roles per organization
        // number in the tens, so fetching them once and matching in memory avoids a join that would
        // otherwise fan out one row per user-role pair and need collapsing again.
        var roles = await context.Roles
            .AsNoTracking()
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var search = request.SearchTerm?.Trim();

        var query = context.Users
            .AsNoTracking()
            .WhereIfNotNull(request.Status, u => u.Status == request.Status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF.Functions.Like rather than Contains so the comparison uses the database collation,
            // which is case-insensitive by default on SQL Server. Contains translates to CHARINDEX
            // and behaves differently under a case-sensitive collation.
            var pattern = $"%{search}%";

            // Email is mapped through a value converter, so `u.Email.Value` is not translatable —
            // EF sees the converted column, not the value object's members. Comparing the whole
            // value object is translatable, so an email search matches exactly rather than
            // partially. That is a real limitation, and the honest fix is to model EmailAddress as
            // an EF complex type so its members become queryable; noted rather than papered over.
            var exactEmail = EmailAddress.Create(search);

            query = exactEmail.IsSuccess
                ? query.Where(u =>
                    EF.Functions.Like(u.FirstName, pattern)
                    || EF.Functions.Like(u.LastName, pattern)
                    || u.Email == exactEmail.Value)
                : query.Where(u =>
                    EF.Functions.Like(u.FirstName, pattern)
                    || EF.Functions.Like(u.LastName, pattern));
        }

        if (request.RoleId is { } roleId)
        {
            // Addresses the mapped backing field: User.RoleIds is exposed read-only and therefore
            // unmapped, so it cannot appear in a translatable expression. See EntityFields.
            query = query.Where(u =>
                EF.Property<List<Guid>>(u, EntityFields.UserRoleIds).Contains(roleId));
        }

        // Projected to scalars only. Resolving role names inside the expression tree would embed a
        // client-side dictionary in a query EF must translate to SQL, which fails at runtime; the
        // names are attached after materialisation instead.
        // Sorted on the entity, before projection. Ordering a projected queryable makes EF compose
        // the sort over the projection's constructor expression, which it cannot translate — the
        // first list endpoint in the codebase failed exactly that way.
        //
        // The default is descending by creation, putting the most recently added people first. Id
        // is time-ordered (UUIDv7) and unique, so it doubles as a stable tiebreaker; without one,
        // rows that tie on the sort key can appear on two pages and skip a third.
        var sorted = query
            .ApplySort(request.SortBy, request.SortDirection, u => u.CreatedOnUtc)
            .ThenByDescending(u => u.Id);

        // u.Email is projected whole, not as u.Email.Value: the converter maps the value object to
        // its column, but member access on it is not translatable. The string is taken after
        // materialisation instead.
        var projected = sorted.Select(u => new UserRow(
            u.Id,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Status,
            u.EmailConfirmed,
            u.LastSignedInOnUtc,
            u.CreatedOnUtc,
            EF.Property<List<Guid>>(u, EntityFields.UserRoleIds)));

        var page = await projected.ToPagedResultAsync(request, cancellationToken);

        var items = page.Items
            .Select(u => new UserListItemDto(
                u.Id,
                u.Email.Value,
                u.FirstName,
                u.LastName,
                $"{u.FirstName} {u.LastName}".Trim(),
                u.Status,
                u.EmailConfirmed,
                u.LastSignedInOnUtc,
                u.CreatedOnUtc,
                u.RoleIds
                    .Select(id => roles.GetValueOrDefault(id, "Unknown"))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        return Result.Success(
            new PagedResult<UserListItemDto>(items, page.Page, page.PageSize, page.TotalCount));
    }

    /// <summary>
    /// The shape actually read from the database: scalars plus raw role identifiers.
    /// </summary>
    /// <remarks>
    /// Property names mirror <see cref="UserListItemDto"/> so that a client-supplied sort field
    /// validated against the public contract resolves against this row without a translation table
    /// that could silently drift out of step.
    /// </remarks>
    private sealed record UserRow(
        Guid Id,
        EmailAddress Email,
        string FirstName,
        string LastName,
        UserStatus Status,
        bool EmailConfirmed,
        DateTimeOffset? LastSignedInOnUtc,
        DateTimeOffset CreatedOnUtc,
        IReadOnlyCollection<Guid> RoleIds);
}

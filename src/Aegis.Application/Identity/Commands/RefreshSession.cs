using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Requests;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Identity.Commands;

/// <summary>Exchanges a refresh token for a new access and refresh token pair.</summary>
/// <remarks>
/// <b>Opts out of the ambient transaction for the same reason as sign-in.</b> Detecting a replayed
/// token revokes the whole chain and then returns a failure. Under <c>UnitOfWorkBehavior</c> that
/// revocation is rolled back together with the rejection, leaving the attacker's stolen token
/// usable on the very next attempt — the response says no while the state says yes.
/// </remarks>
/// <param name="RefreshToken">The opaque refresh token previously issued to the client.</param>
public sealed record RefreshSessionCommand(string RefreshToken)
    : ICommand<AuthenticationResultDto>, ITransactionless;

/// <summary>Validates <see cref="RefreshSessionCommand"/>.</summary>
public sealed class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    /// <summary>Initialises the validator.</summary>
    public RefreshSessionCommandValidator() =>
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(512);
}

/// <summary>Handles <see cref="RefreshSessionCommand"/>.</summary>
/// <remarks>
/// <para>
/// The token is looked up <em>by its hash</em>, never by a plaintext comparison, because only the
/// hash is stored. That also means the lookup is an indexed equality match rather than a scan.
/// </para>
/// <para>
/// Every failure path returns the same error. A client presenting a bad token gets no information
/// about whether it was unknown, expired, revoked, or belonged to a locked account — distinguishing
/// them would let an attacker probe the state of stolen tokens.
/// </para>
/// </remarks>
internal sealed class RefreshSessionCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    ITokenService tokenService,
    IRequestContext requestContext,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshSessionCommand, AuthenticationResultDto>
{
    /// <inheritdoc />
    public async Task<Result<AuthenticationResultDto>> Handle(
        RefreshSessionCommand request,
        CancellationToken cancellationToken)
    {
        var presentedHash = tokenService.HashRefreshToken(request.RefreshToken);

        // No tenant is established: the caller's access token may well have expired, which is why
        // they are refreshing. The refresh token itself identifies the user and hence the tenant.
        var user = await context.Users
            .IgnoreQueryFilters()
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(
                u => u.RefreshTokens.Any(t => t.TokenHash == presentedHash) && !u.IsDeleted,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidRefreshToken);
        }

        var now = timeProvider.GetUtcNow();
        var replacement = tokenService.IssueRefreshToken();

        // The aggregate owns the decision, including detecting replay of an already-rotated token
        // and revoking the whole chain when it happens.
        var rotation = user.RotateRefreshToken(
            presentedHash,
            replacement.Hash,
            now,
            tokenService.RefreshTokenLifetime,
            requestContext.IpAddress);

        if (rotation.IsFailure)
        {
            // Persisted even on failure: a detected reuse has just revoked the chain, and that
            // revocation must survive. Returning without saving would leave the attacker's stolen
            // token usable on the next attempt.
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidRefreshToken);
        }

        var organization = await context.Organizations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(o => o.Id == user.OrganizationId, cancellationToken);

        if (organization is null || !organization.IsOperational)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.OrganizationSuspended);
        }

        tenantContext.SetTenant(user.OrganizationId);

        await context.SaveChangesAsync(cancellationToken);

        var roleIds = user.RoleIds.ToArray();

        var roles = await context.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        // Permissions are re-read on every refresh rather than copied from the old token. This is
        // what bounds how long a revoked capability survives: at most one access-token lifetime,
        // instead of indefinitely.
        var permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var accessToken = tokenService.IssueAccessToken(user, permissions);

        return Result.Success(new AuthenticationResultDto(
            accessToken.Value,
            replacement.Value,
            accessToken.ExpiresOnUtc,
            AuthenticationResultDto.BearerTokenType,
            new AuthenticatedUserDto(
                user.Id,
                user.Email.Value,
                user.DisplayName,
                organization.Id,
                organization.Name,
                roles.Select(r => r.Name).ToArray(),
                permissions)));
    }
}

/// <summary>Revokes the caller's sessions.</summary>
/// <param name="RefreshToken">
/// The session to end. When omitted, every session for the user is ended.
/// </param>
public sealed record SignOutCommand(string? RefreshToken) : ICommand;

/// <summary>Handles <see cref="SignOutCommand"/>.</summary>
/// <remarks>
/// Always reports success, even when the token is unrecognised. Sign-out is not a place to tell a
/// caller whether a token was real, and a client that has already discarded its token should not
/// see an error for trying to tidy up.
/// </remarks>
internal sealed class SignOutCommandHandler(
    IAegisDbContext context,
    Abstractions.Security.ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider) : ICommandHandler<SignOutCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(SignOutCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;

        if (userId is null)
        {
            return Result.Success();
        }

        var user = await context.Users
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user is null)
        {
            return Result.Success();
        }

        var now = timeProvider.GetUtcNow();

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            // "Sign out everywhere". The right default after a suspected compromise, and the only
            // behaviour that makes sense when the client cannot produce its token.
            user.RevokeAllRefreshTokens("Signed out", now);
        }
        else
        {
            var hash = tokenService.HashRefreshToken(request.RefreshToken);

            var token = user.RefreshTokens.SingleOrDefault(t =>
                string.Equals(t.TokenHash, hash, StringComparison.Ordinal));

            if (token is not null && token.IsActive(now))
            {
                user.RevokeAllRefreshTokens("Signed out", now);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

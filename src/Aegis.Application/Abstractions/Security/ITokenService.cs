using Aegis.Domain.Identity;

namespace Aegis.Application.Abstractions.Security;

/// <summary>A newly issued access token and its expiry.</summary>
/// <param name="Value">The signed JWT.</param>
/// <param name="ExpiresOnUtc">When the token stops being accepted.</param>
public readonly record struct AccessToken(string Value, DateTimeOffset ExpiresOnUtc);

/// <summary>
/// A refresh token in both the forms the system needs it.
/// </summary>
/// <param name="Value">The opaque token handed to the client. Never persisted.</param>
/// <param name="Hash">The digest stored against the user. Never leaves the server.</param>
/// <remarks>
/// The pair exists so that the two halves cannot be confused. Persisting <c>Value</c> would make a
/// database backup equivalent to a set of live sessions, and returning <c>Hash</c> to the client
/// would issue a credential that can never be verified.
/// </remarks>
public readonly record struct RefreshTokenPair(string Value, string Hash);

/// <summary>
/// Issues and inspects authentication tokens.
/// </summary>
/// <remarks>
/// <para>
/// The two token types have deliberately different characters. The <b>access token</b> is a signed
/// JWT carrying the user's identity, organization and permissions; it is verified without a
/// database round trip, which is what makes authorization cheap, and it therefore cannot be
/// revoked before it expires. Its lifetime is short for exactly that reason.
/// </para>
/// <para>
/// The <b>refresh token</b> is an opaque random value with no structure and no claims. It is
/// checked against stored state on every use, so it can be revoked instantly — which is what makes
/// sign-out, lockout and reuse detection meaningful.
/// </para>
/// </remarks>
public interface ITokenService
{
    /// <summary>Issues a signed access token for the user.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="permissions">Effective permissions, resolved from the user's roles.</param>
    AccessToken IssueAccessToken(User user, IReadOnlyCollection<string> permissions);

    /// <summary>Generates a cryptographically random refresh token and its stored digest.</summary>
    RefreshTokenPair IssueRefreshToken();

    /// <summary>Computes the stored digest of a refresh token presented by a client.</summary>
    string HashRefreshToken(string refreshToken);

    /// <summary>How long an issued refresh token remains valid.</summary>
    TimeSpan RefreshTokenLifetime { get; }
}

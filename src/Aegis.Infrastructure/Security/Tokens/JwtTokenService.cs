using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aegis.Infrastructure.Security.Tokens;

/// <summary>JWT configuration, bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Minimum signing key length in bytes, imposed by HMAC-SHA256.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Token issuer.</summary>
    public string Issuer { get; set; } = "aegis";

    /// <summary>Intended audience.</summary>
    public string Audience { get; set; } = "aegis-api";

    /// <summary>Symmetric signing key. Supplied by configuration; never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes.
    /// </summary>
    /// <remarks>
    /// Short by design. An access token is verified from its signature alone with no database
    /// round trip, which is what makes authorization cheap and also what makes it impossible to
    /// revoke before expiry. Fifteen minutes bounds how long a revoked permission or a deactivated
    /// account remains effective.
    /// </remarks>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token lifetime in days.
    /// </summary>
    /// <remarks>
    /// Long enough that a field technician is not re-authenticating mid-shift, short enough that an
    /// abandoned session eventually dies. Rotation on every use is what actually limits exposure.
    /// </remarks>
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>
/// Issues signed access tokens and opaque refresh tokens.
/// </summary>
/// <remarks>
/// The asymmetry between the two token types is the whole design. The access token is a
/// self-contained JWT — fast to verify, impossible to revoke. The refresh token is an opaque random
/// value with no claims at all, checked against stored state on every use — slower, but revocable
/// instantly, which is what makes sign-out and reuse detection mean anything.
/// </remarks>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises the service and validates the signing key.</summary>
    /// <exception cref="InvalidOperationException">The signing key is missing or too short.</exception>
    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _timeProvider = timeProvider;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Refusing to start rather than fall back to a " +
                "default key, which would let anyone holding this source forge tokens.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);

        // HMAC-SHA256 requires a key at least as long as its output. A shorter key is rejected at
        // startup rather than at first sign-in, so the failure surfaces during deployment.
        if (keyBytes.Length < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes " +
                $"({JwtOptions.MinimumSigningKeyBytes} ASCII characters) for HMAC-SHA256.");
        }

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    /// <inheritdoc />
    public AccessToken IssueAccessToken(User user, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(permissions);

        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AegisClaims.UserId, user.Id.ToString()),
            new(AegisClaims.Email, user.Email.Value),
            new(AegisClaims.DisplayName, user.DisplayName),

            // The tenant. Signed, so it cannot be altered by the client — which is exactly why the
            // tenant is read from here and never from a header or route value.
            new(AegisClaims.OrganizationId, user.OrganizationId.ToString()),

            // Lets a token be invalidated ahead of its expiry when the user's security posture
            // changes: the stored stamp rotates and the embedded one no longer matches.
            new(AegisClaims.SecurityStamp, user.SecurityStamp),

            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        // Permissions are embedded so authorization needs no database round trip. The cost is token
        // size: an administrator carries every permission, which is why names are short and
        // dot-delimited rather than verbose.
        claims.AddRange(permissions.Select(p => new Claim(AegisClaims.Permission, p)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 256 bits from a cryptographic RNG, with no structure and no encoded meaning. A refresh token
    /// is only ever compared against stored state, so there is nothing for it to carry — and
    /// anything it did carry would be one more thing an attacker could read out of a stolen token.
    /// </remarks>
    public RefreshTokenPair IssueRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Convert.ToBase64String(bytes);

        return new RefreshTokenPair(value, HashRefreshToken(value));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain SHA-256, deliberately, where passwords get 600,000 PBKDF2 iterations. The reasoning
    /// is that a refresh token is 256 bits of uniform randomness, so there is no dictionary to
    /// attack and brute force is infeasible regardless of hash speed. Passwords are low-entropy and
    /// human-chosen, which is what makes a slow KDF necessary there. Using a slow KDF here would
    /// add latency to every refresh for no security gain.
    /// </remarks>
    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));

        return Convert.ToBase64String(hash);
    }
}

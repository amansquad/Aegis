namespace Aegis.Api.Authorization;

/// <summary>Names of the rate limiting policies applied to endpoints.</summary>
/// <remarks>
/// Constants rather than inline strings, because a typo in an <c>[EnableRateLimiting]</c> attribute
/// throws only when that endpoint is first hit — which in practice means it is discovered in
/// production, on the endpoint that most needed the limit.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>Sign-in and session renewal.</summary>
    public const string Authentication = "authentication";

    /// <summary>Organization registration.</summary>
    public const string Registration = "registration";
}

/// <summary>Rate limit budgets, bound from the <c>RateLimiting</c> configuration section.</summary>
/// <remarks>
/// <para>
/// Configurable rather than hard-coded because the right budget depends on deployment shape. An API
/// behind a corporate NAT sees an entire office as one address, and a limit tuned for individual
/// users would lock out a whole building.
/// </para>
/// <para>
/// It is also what lets the integration suite run: every test shares one source address, so
/// production budgets would exhaust the registration allowance within the first few tests and fail
/// everything after them. The suite raises the limits and then verifies the limiter separately,
/// which tests the mechanism without letting it interfere with unrelated assertions.
/// </para>
/// </remarks>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Requests permitted per window on sign-in and refresh.</summary>
    public int AuthenticationPermitLimit { get; set; } = 10;

    /// <summary>Window length in seconds for the authentication policy.</summary>
    public int AuthenticationWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Requests permitted per window on registration.
    /// </summary>
    /// <remarks>
    /// Tighter than authentication because registration creates a tenant, seeds five roles and
    /// performs a full key derivation, making it the most expensive anonymous endpoint in the API.
    /// </remarks>
    public int RegistrationPermitLimit { get; set; } = 3;

    /// <summary>Window length in seconds for the registration policy.</summary>
    public int RegistrationWindowSeconds { get; set; } = 600;
}

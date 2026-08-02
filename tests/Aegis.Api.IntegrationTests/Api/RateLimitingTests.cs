using System.Net;
using System.Net.Http.Json;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Identity.Commands;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Verifies that the authentication endpoints are rate limited.
/// </summary>
/// <remarks>
/// <para>
/// Account lockout defends one account against many guesses. It is structurally blind to the
/// opposite attack — credential spraying, where a single common password is tried against thousands
/// of accounts. Each account records one failure, no lockout ever fires, and a small percentage of
/// accounts fall. Limiting by source address is the control that sees it.
/// </para>
/// <para>
/// The test host raises the budgets so the limiter does not interfere with unrelated assertions,
/// so this asserts the mechanism is wired and enforcing rather than asserting a specific
/// production threshold.
/// </para>
/// </remarks>
public sealed class RateLimitingTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [DockerFact]
    public async Task The_authentication_endpoints_reject_a_burst_from_one_source()
    {
        using var client = CreateAnonymousClient();

        // The refresh endpoint is the cheapest limited endpoint: an unknown token is a single
        // indexed miss, with no key derivation. Hammering login instead would spend several seconds
        // on PBKDF2 proving a point about the limiter rather than about hashing.
        var attempts = AegisWebApplicationFactory.TestAuthenticationPermitLimit + 25;
        var rejected = 0;
        HttpResponseMessage? lastRejection = null;

        for (var i = 0; i < attempts; i++)
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/refresh", UriKind.Relative),
                new RefreshSessionCommand($"definitely-not-a-real-token-{i}"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected++;
                lastRejection = response;
            }
        }

        rejected.ShouldBeGreaterThan(
            0,
            "a burst well past the configured budget should have been throttled");

        lastRejection.ShouldNotBeNull();

        // Retry-After turns a rejection into something a well-behaved client can act on, instead of
        // something it retries immediately and makes worse.
        lastRejection.Headers.RetryAfter.ShouldNotBeNull();
    }

    [DockerFact]
    public async Task A_throttled_response_carries_a_problem_details_body()
    {
        using var client = CreateAnonymousClient();

        HttpResponseMessage? throttled = null;

        for (var i = 0; i < AegisWebApplicationFactory.TestAuthenticationPermitLimit + 25; i++)
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/refresh", UriKind.Relative),
                new RefreshSessionCommand($"another-fake-token-{i}"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }
        }

        throttled.ShouldNotBeNull();

        // One error shape across the whole API, including the responses produced by middleware
        // rather than by a handler.
        var body = await throttled.Content.ReadAsStringAsync();
        body.ShouldContain("Too many requests");
    }
}

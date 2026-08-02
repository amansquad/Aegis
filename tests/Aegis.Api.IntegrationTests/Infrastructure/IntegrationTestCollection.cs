using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Domain.Organizations;

namespace Aegis.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shares one container-backed host across every integration test.
/// </summary>
/// <remarks>
/// xUnit creates a new class instance per test method but a collection fixture exactly once. Given
/// that starting SQL Server costs several seconds, per-class fixtures would turn the suite into
/// something people run only in CI — and a test suite developers avoid stops catching regressions
/// at the point they are cheapest to fix.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<AegisWebApplicationFactory>
{
    /// <summary>The collection name applied to integration test classes.</summary>
    public const string Name = "Aegis integration";
}

/// <summary>A tenant provisioned for a test, with its administrator's live tokens.</summary>
/// <param name="OrganizationId">The created organization.</param>
/// <param name="OrganizationName">Its display name.</param>
/// <param name="UserId">The administrator's identifier.</param>
/// <param name="Email">The administrator's email address.</param>
/// <param name="Password">The administrator's password, retained so tests can sign in again.</param>
/// <param name="Authentication">The tokens returned at registration.</param>
public sealed record ProvisionedTenant(
    Guid OrganizationId,
    string OrganizationName,
    Guid UserId,
    string Email,
    string Password,
    AuthenticationResultDto Authentication);

/// <summary>Base class supplying the shared host and authentication helpers.</summary>
/// <remarks>
/// <para>
/// There is deliberately no test-only authentication scheme. Now that the identity module issues
/// real tokens, tests authenticate the way a client does — by registering and signing in — so the
/// JWT pipeline, the permission handler and the tenant middleware are all genuinely exercised.
/// </para>
/// <para>
/// A stub scheme would have tested a code path that does not exist in production, which is the
/// failure mode where the suite is green and the deployed system rejects every request.
/// </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(AegisWebApplicationFactory factory)
{
    /// <summary>The container-backed host under test.</summary>
    protected AegisWebApplicationFactory Factory { get; } = factory;

    /// <summary>
    /// Serializer options mirroring the API's own.
    /// </summary>
    /// <remarks>
    /// The API writes enums as strings, so a client deserialising with the defaults fails on the
    /// first enum-valued field. Mirroring the server's configuration here also means a change to it
    /// breaks these tests, which is the correct place for that mismatch to surface — a real client
    /// would hit it too.
    /// </remarks>
    protected static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Creates a client that sends no credentials.</summary>
    protected HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>
    /// Registers a fresh organization and returns it together with its administrator's tokens.
    /// </summary>
    /// <remarks>
    /// Each call produces a uniquely named organization, so tests never collide on the
    /// platform-wide unique slug and can run in any order.
    /// </remarks>
    /// <param name="namePrefix">Prefix for the generated organization name.</param>
    protected async Task<ProvisionedTenant> ProvisionTenantAsync(string namePrefix = "Utility")
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..12];
        var organizationName = $"{namePrefix} {suffix}";
        var email = $"admin.{suffix}@aegis.test";
        // Not "correct-horse-battery-staple": the password policy now bans it, which it should —
        // the xkcd example is one of the most published passphrases in existence. This one is
        // long, unlisted, and shares no fragment with the generated organization name or email.
        const string password = "thistle marmalade quiet lantern";

        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new RegisterOrganizationCommand(
                organizationName,
                OrganizationKind.WaterUtility,
                "Etc/UTC",
                email,
                password,
                "Ada",
                "Osei"));

        // EnsureSuccessStatusCode discards the body, which under the Development environment is
        // exactly where the ProblemDetails carrying the server-side exception lives. Surfacing it
        // turns "500 Internal Server Error" into an actionable message.
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"Registration failed with {(int)response.StatusCode} {response.StatusCode}: {problem}");
        }

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>()
            ?? throw new InvalidOperationException("Registration returned no body.");

        return new ProvisionedTenant(
            result.User.OrganizationId,
            organizationName,
            result.User.Id,
            email,
            password,
            result);
    }

    /// <summary>Creates a client carrying the supplied access token.</summary>
    protected HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = Factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(AuthenticationResultDto.BearerTokenType, accessToken);

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    /// <summary>Registers a tenant and returns a client authenticated as its administrator.</summary>
    protected async Task<(HttpClient Client, ProvisionedTenant Tenant)> CreateTenantClientAsync(
        string namePrefix = "Utility")
    {
        var tenant = await ProvisionTenantAsync(namePrefix);

        return (CreateAuthenticatedClient(tenant.Authentication.AccessToken), tenant);
    }
}

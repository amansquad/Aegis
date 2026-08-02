using System.Net.Http.Headers;

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

/// <summary>Base class supplying the shared host and helpers for building authenticated clients.</summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(AegisWebApplicationFactory factory)
{
    /// <summary>The container-backed host under test.</summary>
    protected AegisWebApplicationFactory Factory { get; } = factory;

    /// <summary>Creates a client that sends no credentials.</summary>
    protected HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>
    /// Creates a client authenticated as a user within the supplied organization.
    /// </summary>
    /// <param name="organizationId">The tenant the caller belongs to.</param>
    /// <param name="userId">The acting user, generated when omitted.</param>
    /// <param name="permissions">Permissions to grant.</param>
    protected HttpClient CreateClientFor(
        Guid organizationId,
        Guid? userId = null,
        params string[] permissions)
    {
        var client = Factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeader,
            (userId ?? Guid.CreateVersion7()).ToString());

        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.OrganizationHeader,
            organizationId.ToString());

        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthenticationHandler.PermissionsHeader,
                string.Join(',', permissions));
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }
}

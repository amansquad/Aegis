using System.Net;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Infrastructure.Security;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Verifies the HTTP pipeline: health probes, correlation id propagation and error shape.
/// </summary>
public sealed class PipelineTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Guid Organization = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [DockerFact]
    public async Task Liveness_succeeds_without_touching_any_dependency()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DockerFact]
    public async Task Readiness_succeeds_when_the_database_is_reachable()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DockerFact]
    public async Task An_inbound_correlation_id_is_echoed_on_the_response()
    {
        // What lets a user quote an identifier that finds the exact request across every log sink.
        using var client = CreateAnonymousClient();
        var correlationId = "trace-abc-123";

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add(RequestContext.CorrelationIdHeader, correlationId);

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues(RequestContext.CorrelationIdHeader, out var echoed).ShouldBeTrue();
        echoed!.Single().ShouldBe(correlationId);
    }

    [DockerFact]
    public async Task A_correlation_id_is_generated_when_the_caller_supplies_none()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.TryGetValues(RequestContext.CorrelationIdHeader, out var generated).ShouldBeTrue();
        generated!.Single().ShouldNotBeNullOrWhiteSpace();
    }

    [DockerFact]
    public async Task A_hostile_correlation_id_is_sanitised_before_it_reaches_the_logs()
    {
        // Log injection: newlines in a header written to log output can fabricate convincing log
        // entries. The middleware strips control characters and caps the length.
        using var client = CreateAnonymousClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(
            RequestContext.CorrelationIdHeader,
            "abc\r\nFAKE-LOG-LINE evil");

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues(RequestContext.CorrelationIdHeader, out var echoed).ShouldBeTrue();

        var value = echoed!.Single();
        value.ShouldNotContain("\n");
        value.ShouldNotContain("\r");
        value.ShouldNotContain(" ");
    }

    [DockerFact]
    public async Task An_over_long_correlation_id_is_truncated()
    {
        using var client = CreateAnonymousClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(
            RequestContext.CorrelationIdHeader,
            new string('a', 5000));

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues(RequestContext.CorrelationIdHeader, out var echoed).ShouldBeTrue();
        echoed!.Single().Length.ShouldBeLessThanOrEqualTo(64);
    }

    [DockerFact]
    public async Task An_unknown_route_returns_a_problem_details_payload()
    {
        using var client = CreateClientFor(Organization);

        var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DockerFact]
    public async Task Swagger_is_served_in_the_development_environment()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Aegis Infrastructure Management API");
    }
}

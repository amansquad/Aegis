using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Domain.Auditing;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves that EF Core global query filters isolate tenants against a real SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// These are the most important tests in the repository. Every other defect produces a wrong
/// answer; a failure here hands one customer another customer's data.
/// </para>
/// <para>
/// The subtlety being verified is specific. EF Core caches the compiled model across
/// <c>DbContext</c> instances, while the tenant differs per request. If the filter captured the
/// tenant value at model-build time rather than re-reading it per query, the first request's
/// organization would be baked into the cached model and every later request would silently read
/// that organization's rows. Reasoning about this is not enough — hence a test that runs the
/// queries through separate scopes against a real provider.
/// </para>
/// </remarks>
public sealed class TenantIsolationTests(AegisWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private static readonly Guid NorthernWater = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SouthernPower = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [DockerFact]
    public async Task A_query_returns_only_rows_belonging_to_the_current_organization()
    {
        var northernMarker = $"north-{Guid.CreateVersion7():N}";
        var southernMarker = $"south-{Guid.CreateVersion7():N}";

        await SeedAsync(NorthernWater, northernMarker);
        await SeedAsync(SouthernPower, southernMarker);

        var northernRows = await ReadEntityNamesAsync(NorthernWater);
        var southernRows = await ReadEntityNamesAsync(SouthernPower);

        northernRows.ShouldContain(northernMarker);
        southernRows.ShouldContain(southernMarker);

        // The assertion that matters: neither organization can see the other's row, even though
        // both live in the same table and the query carries no explicit tenant predicate.
        northernRows.ShouldNotContain(southernMarker);
        southernRows.ShouldNotContain(northernMarker);
    }

    [DockerFact]
    public async Task The_filter_is_re_evaluated_per_scope_and_not_baked_into_the_cached_model()
    {
        var marker = $"cache-probe-{Guid.CreateVersion7():N}";

        await SeedAsync(NorthernWater, marker);

        // First read establishes the compiled model under one tenant.
        (await ReadEntityNamesAsync(NorthernWater)).ShouldContain(marker);

        // Second read, different scope, different tenant, same cached model. If the tenant had
        // been captured at model-build time this would still return the northern row.
        (await ReadEntityNamesAsync(SouthernPower)).ShouldNotContain(marker);

        // Third read proves the first tenant still works, ruling out the filter simply breaking.
        (await ReadEntityNamesAsync(NorthernWater)).ShouldContain(marker);
    }

    [DockerFact]
    public async Task A_scope_with_no_tenant_sees_no_rows_at_all()
    {
        // Fail-closed. The alternative convention — treating "no tenant" as "no filter" — turns a
        // missing organization claim into a full cross-tenant disclosure.
        var marker = $"unscoped-{Guid.CreateVersion7():N}";
        await SeedAsync(NorthernWater, marker);

        await using var scope = Factory.CreateTenantScope(organizationId: null);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var rows = await context.AuditTrail.AsNoTracking().ToListAsync();

        rows.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task Counting_and_projecting_are_filtered_as_well_as_listing()
    {
        // A filter applied only to plain listing would still leak totals through an aggregate,
        // which is enough to disclose the size and activity of another operator's estate.
        var marker = $"aggregate-{Guid.CreateVersion7():N}";
        await SeedAsync(NorthernWater, marker);

        await using var scope = Factory.CreateTenantScope(SouthernPower);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var matching = await context.AuditTrail
            .Where(e => e.EntityName == marker)
            .CountAsync();

        var projected = await context.AuditTrail
            .Where(e => e.EntityName == marker)
            .Select(e => e.Id)
            .ToListAsync();

        var exists = await context.AuditTrail.AnyAsync(e => e.EntityName == marker);

        matching.ShouldBe(0);
        projected.ShouldBeEmpty();
        exists.ShouldBeFalse();
    }

    [DockerFact]
    public async Task A_row_cannot_be_fetched_by_id_from_another_organization()
    {
        // Direct lookup by primary key is the path a hand-written endpoint is most likely to take,
        // and an unfiltered FirstOrDefault by id is the classic insecure-direct-object-reference.
        var marker = $"byid-{Guid.CreateVersion7():N}";
        var id = await SeedAsync(NorthernWater, marker);

        await using var scope = Factory.CreateTenantScope(SouthernPower);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var found = await context.AuditTrail.FirstOrDefaultAsync(e => e.Id == id);

        found.ShouldBeNull();
    }

    [DockerFact]
    public async Task Ignoring_query_filters_reveals_rows_across_tenants()
    {
        // Documents the escape hatch honestly rather than pretending it does not exist.
        // IgnoreQueryFilters is a deliberate, reviewable call reserved for system operations; this
        // test exists so that its power is visible in the suite rather than discovered in an incident.
        var marker = $"bypass-{Guid.CreateVersion7():N}";
        await SeedAsync(NorthernWater, marker);

        await using var scope = Factory.CreateTenantScope(SouthernPower);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var visible = await context.AuditTrail
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityName == marker);

        visible.ShouldBeTrue();
    }

    /// <summary>Inserts one audit row under the supplied organization and returns its id.</summary>
    private async Task<Guid> SeedAsync(Guid organizationId, string entityName)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var entry = AuditTrailEntry.Record(
            organizationId,
            entityName,
            Guid.CreateVersion7().ToString(),
            AuditAction.Created,
            DateTimeOffset.UtcNow);

        context.AuditTrail.Add(entry);
        await context.SaveChangesAsync();

        return entry.Id;
    }

    private async Task<List<string>> ReadEntityNamesAsync(Guid organizationId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        return await context.AuditTrail
            .AsNoTracking()
            .Select(e => e.EntityName)
            .ToListAsync();
    }
}

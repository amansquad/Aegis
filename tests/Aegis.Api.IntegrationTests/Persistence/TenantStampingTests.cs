using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Domain.Auditing;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Persistence;

/// <summary>
/// Verifies that <c>PersistenceMetadataInterceptor</c> assigns tenant ownership on insert and
/// refuses to let it change afterwards.
/// </summary>
public sealed class TenantStampingTests(AegisWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private static readonly Guid OwningOrganization = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherOrganization = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [DockerFact]
    public async Task An_insert_that_omits_the_organization_is_stamped_with_the_current_tenant()
    {
        var marker = $"stamp-{Guid.CreateVersion7():N}";

        await using (var scope = Factory.CreateTenantScope(OwningOrganization))
        {
            var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

            // Guid.Empty stands in for "the caller did not set it", which is what a handler that
            // does not know tenancy exists would produce.
            var entry = AuditTrailEntry.Record(
                Guid.Empty,
                marker,
                Guid.CreateVersion7().ToString(),
                AuditAction.Created,
                DateTimeOffset.UtcNow);

            context.AuditTrail.Add(entry);
            await context.SaveChangesAsync();
        }

        await using var verifyScope = Factory.CreateTenantScope(OwningOrganization);
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var stored = await verifyContext.AuditTrail
            .AsNoTracking()
            .SingleAsync(e => e.EntityName == marker);

        stored.OrganizationId.ShouldBe(OwningOrganization);
    }

    [DockerFact]
    public async Task Reassigning_the_organization_on_an_existing_row_is_ignored()
    {
        // Moving a row between tenants is never a legitimate outcome of an ordinary update. The
        // interceptor resets the property to its original value, so an attempt is a silent no-op
        // rather than a data breach.
        var marker = $"reassign-{Guid.CreateVersion7():N}";
        Guid id;

        await using (var seedScope = Factory.CreateTenantScope(OwningOrganization))
        {
            var context = seedScope.ServiceProvider.GetRequiredService<AegisDbContext>();

            var entry = AuditTrailEntry.Record(
                OwningOrganization,
                marker,
                Guid.CreateVersion7().ToString(),
                AuditAction.Created,
                DateTimeOffset.UtcNow);

            context.AuditTrail.Add(entry);
            await context.SaveChangesAsync();
            id = entry.Id;
        }

        await using (var mutateScope = Factory.CreateTenantScope(OwningOrganization))
        {
            var context = mutateScope.ServiceProvider.GetRequiredService<AegisDbContext>();

            var tracked = await context.AuditTrail.SingleAsync(e => e.Id == id);

            context.Entry(tracked).Property(nameof(AuditTrailEntry.OrganizationId)).CurrentValue =
                OtherOrganization;

            await context.SaveChangesAsync();
        }

        // Still visible to the original owner.
        await using var ownerScope = Factory.CreateTenantScope(OwningOrganization);
        var ownerContext = ownerScope.ServiceProvider.GetRequiredService<AegisDbContext>();
        (await ownerContext.AuditTrail.AnyAsync(e => e.Id == id)).ShouldBeTrue();

        // And never became visible to the other organization.
        await using var otherScope = Factory.CreateTenantScope(OtherOrganization);
        var otherContext = otherScope.ServiceProvider.GetRequiredService<AegisDbContext>();
        (await otherContext.AuditTrail.AnyAsync(e => e.Id == id)).ShouldBeFalse();
    }

    [DockerFact]
    public async Task Migrations_created_the_audit_schema_rather_than_the_model_snapshot()
    {
        // Guards against a fixture that quietly falls back to EnsureCreated, which would build the
        // schema from the model and let a broken migration ship while the suite stayed green.
        await using var scope = Factory.CreateTenantScope(OwningOrganization);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        applied.ShouldNotBeEmpty();
        applied.ShouldContain(m => m.EndsWith("InitialAuditTrail", StringComparison.Ordinal));
    }
}

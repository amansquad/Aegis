using System.Reflection;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Domain.Common;
using Aegis.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace Aegis.Application.UnitTests.Architecture;

/// <summary>
/// Enforces the dependency rule between layers.
/// </summary>
/// <remarks>
/// Layered architectures do not collapse in one commit. They erode through a series of individually
/// reasonable exceptions, each defensible on the day it is made. These tests remove the judgement
/// call by failing the build instead.
/// </remarks>
public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IAegisDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(AegisDbContext).Assembly;

    [Fact]
    public void Domain_must_not_depend_on_Application()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Aegis.Application")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Domain_must_not_depend_on_Infrastructure()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Aegis.Infrastructure")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Application_must_not_depend_on_Infrastructure()
    {
        // The inversion that makes handlers testable. Application declares ports
        // (IAegisDbContext, ICacheService, ICurrentUser); Infrastructure supplies the adapters.
        // A reference in this direction would put SQL Server and Redis behind every unit test.
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Aegis.Infrastructure")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Application_must_not_depend_on_the_API_layer()
    {
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Aegis.Api")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Domain_must_not_reference_a_specific_database_provider()
    {
        // Aegis.Application takes a documented dependency on the EF Core abstraction package.
        // The domain model takes none at all, and no layer may name the SQL Server provider
        // outside Infrastructure.
        Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Application_must_not_reference_a_specific_database_provider()
    {
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.Data.SqlClient",
                "StackExchange.Redis")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Application_must_not_depend_on_ASP_NET_Core()
    {
        // HTTP is a delivery mechanism. A handler that knows about HttpContext cannot be reused by
        // a background job, the SignalR hub, or the offline sync reconciler.
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Pipeline_behaviours_must_be_sealed()
    {
        // These run on every request. Leaving them open to inheritance invites a subclass that
        // overrides half the pipeline's behaviour in a way no reader of the pipeline expects.
        Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace("Aegis.Application.Behaviors")
            .And()
            .AreClasses()
            .Should()
            .BeSealed()
            .GetResult()
            .ShouldBeSuccessful();
    }

    [Fact]
    public void Entity_configurations_must_be_internal_to_Infrastructure()
    {
        // Mapping is an infrastructure detail. A public configuration class invites a module to
        // reach into another module's schema, which is exactly the coupling the module boundary
        // exists to prevent.
        var publicConfigurations = InfrastructureAssembly
            .GetTypes()
            .Where(t => t.Namespace?.Contains("Persistence.Configurations", StringComparison.Ordinal) == true)
            .Where(t => t.IsPublic)
            .Select(t => t.Name)
            .ToArray();

        publicConfigurations.ShouldBeEmpty(
            $"Entity configurations should be internal, but these are public: " +
            $"{string.Join(", ", publicConfigurations)}");
    }
}

/// <summary>Shouldly assertion for a NetArchTest result.</summary>
internal static class ArchTestResultExtensions
{
    public static void ShouldBeSuccessful(this TestResult result)
    {
        var offenders = result.FailingTypeNames ?? [];

        result.IsSuccessful.ShouldBeTrue(
            $"Dependency rule violated by: {string.Join(", ", offenders)}");
    }
}

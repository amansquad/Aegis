using System.Reflection;
using Aegis.Domain.Common;

namespace Aegis.Domain.UnitTests.Architecture;

/// <summary>
/// Executable enforcement of the dependency rule.
/// </summary>
/// <remarks>
/// Architecture documented in a README decays; architecture asserted in CI does not. These tests
/// fail the build the moment someone adds an EF Core reference to the domain "just for an
/// attribute" — which is exactly how layered architectures rot, one reasonable-sounding exception
/// at a time.
/// </remarks>
public sealed class DependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;

    /// <summary>
    /// The allow-list of assemblies the domain may reference: the BCL and nothing else.
    /// </summary>
    private static readonly string[] PermittedReferencePrefixes =
    [
        "System",
        "netstandard",
        "mscorlib",
        "Microsoft.CSharp",
    ];

    [Fact]
    public void Domain_should_reference_nothing_but_the_base_class_library()
    {
        var offenders = DomainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !PermittedReferencePrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Aegis.Domain must stay free of third-party dependencies, but references: " +
            $"{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Aggregate_roots_should_be_the_only_entities_carrying_a_concurrency_token()
    {
        // Optimistic concurrency is an aggregate-level concern: the root guards the consistency
        // boundary, so the version travels with the root. A child entity with its own token would
        // imply it can be saved independently of its root, which contradicts the aggregate rule.
        var versionedTypes = DomainAssembly
            .GetTypes()
            .Where(t => t.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Where(t => !t.IsAbstract)
            .ToArray();

        foreach (var type in versionedTypes)
        {
            IsAggregateRoot(type).ShouldBeTrue(
                $"{type.Name} exposes a concurrency token but is not an aggregate root.");
        }
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}

using Aegis.Application.Common.Extensions;

// Shouldly ships its own SortDirection enum, which collides with ours through the global usings.
using SortDirection = Aegis.Application.Common.Models.SortDirection;

namespace Aegis.Application.UnitTests.Common;

/// <summary>
/// Covers the composition helpers that translate to SQL.
/// </summary>
/// <remarks>
/// These exercise expression composition over an in-memory queryable, which is the right level for
/// sorting and predicate logic. <c>ToPagedResultAsync</c> is deliberately not tested here: it calls
/// EF Core's async operators, which require a real provider, and is covered by the integration
/// suite against SQL Server instead.
/// </remarks>
public sealed class QueryableExtensionsTests
{
    private sealed record AssetRow(Guid Id, string Name, int Capacity, DateTimeOffset InstalledOn)
    {
        public List<string> Tags { get; init; } = [];
    }

    private static readonly AssetRow[] Assets =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Charlie", 300, new DateTimeOffset(2023, 3, 1, 0, 0, 0, TimeSpan.Zero)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Alpha", 100, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Bravo", 200, new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero)),
    ];

    private static IQueryable<AssetRow> Query => Assets.AsQueryable();

    [Fact]
    public void WhereIf_applies_the_predicate_only_when_the_condition_holds()
    {
        Query.WhereIf(true, a => a.Capacity > 150).Count().ShouldBe(2);
        Query.WhereIf(false, a => a.Capacity > 150).Count().ShouldBe(3);
    }

    [Fact]
    public void WhereIfNotBlank_ignores_null_and_whitespace_search_terms()
    {
        Query.WhereIfNotBlank(null, a => a.Name.Contains("Al")).Count().ShouldBe(3);
        Query.WhereIfNotBlank("   ", a => a.Name.Contains("Al")).Count().ShouldBe(3);
        Query.WhereIfNotBlank("Al", a => a.Name.Contains("Al")).Count().ShouldBe(1);
    }

    [Fact]
    public void WhereIfNotNull_ignores_absent_optional_filters()
    {
        int? noFilter = null;
        int? withFilter = 150;

        Query.WhereIfNotNull(noFilter, a => a.Capacity > noFilter).Count().ShouldBe(3);
        Query.WhereIfNotNull(withFilter, a => a.Capacity > withFilter).Count().ShouldBe(2);
    }

    [Fact]
    public void Sorts_ascending_by_a_named_property()
    {
        var sorted = Query.ApplySort("Name", SortDirection.Ascending, a => a.Id).ToList();

        sorted.Select(a => a.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Fact]
    public void Sorts_descending_by_a_named_property()
    {
        var sorted = Query.ApplySort("capacity", SortDirection.Descending, a => a.Id).ToList();

        sorted.Select(a => a.Capacity).ShouldBe([300, 200, 100]);
    }

    [Fact]
    public void Property_matching_is_case_insensitive()
    {
        // Clients send camelCase; the CLR properties are PascalCase. Requiring an exact match would
        // make every JavaScript caller's natural spelling fail.
        Query.ApplySort("name", SortDirection.Ascending, a => a.Id).First().Name.ShouldBe("Alpha");
        Query.ApplySort("NAME", SortDirection.Ascending, a => a.Id).First().Name.ShouldBe("Alpha");
    }

    [Fact]
    public void Falls_back_to_the_default_sort_when_the_property_does_not_exist()
    {
        var sorted = Query.ApplySort("DoesNotExist", SortDirection.Ascending, a => a.Id).ToList();

        sorted.Select(a => a.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Fact]
    public void Falls_back_to_the_default_sort_for_an_untranslatable_property()
    {
        // Sorting by a collection cannot be expressed in SQL and would throw at execution time,
        // turning a mistyped query string into a 500.
        var sorted = Query.ApplySort("Tags", SortDirection.Ascending, a => a.Id).ToList();

        sorted.Select(a => a.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_the_default_sort_when_no_property_is_requested(string? sortBy)
    {
        Query.ApplySort(sortBy, SortDirection.Ascending, a => a.Id).First().Name.ShouldBe("Alpha");
    }

    [Fact]
    public void A_malicious_sort_string_cannot_reach_the_query()
    {
        // The value is resolved through reflection against the CLR type, so it never becomes a SQL
        // fragment. An unmatched name simply falls back to the default ordering.
        var sorted = Query
            .ApplySort("Name; DROP TABLE Assets--", SortDirection.Ascending, a => a.Id)
            .ToList();

        sorted.Count.ShouldBe(3);
        sorted.Select(a => a.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Fact]
    public void Reports_scalar_properties_as_sortable()
    {
        var sortable = QueryableExtensions.GetSortableProperties<AssetRow>();

        sortable.ShouldContain(nameof(AssetRow.Name));
        sortable.ShouldContain(nameof(AssetRow.Capacity));
        sortable.ShouldContain(nameof(AssetRow.InstalledOn));
        sortable.ShouldContain(nameof(AssetRow.Id));
    }

    [Fact]
    public void Does_not_report_collection_properties_as_sortable()
    {
        QueryableExtensions.GetSortableProperties<AssetRow>().ShouldNotContain(nameof(AssetRow.Tags));
    }

    [Theory]
    [InlineData("Name", true)]
    [InlineData("name", true)]
    [InlineData("Tags", false)]
    [InlineData("Nope", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Validates_a_requested_sort_field(string? candidate, bool expected)
    {
        // Used by FluentValidation rules so an unknown sortBy is rejected at the boundary with a
        // clear message, rather than silently ignored and paged through in arbitrary order.
        QueryableExtensions.IsSortableProperty<AssetRow>(candidate).ShouldBe(expected);
    }
}

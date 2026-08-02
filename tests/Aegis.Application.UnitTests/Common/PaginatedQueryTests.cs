using Aegis.Application.Common.Models;

// Shouldly ships its own SortDirection enum, which collides with ours through the global usings.
using SortDirection = Aegis.Application.Common.Models.SortDirection;

namespace Aegis.Application.UnitTests.Common;

public sealed class PaginatedQueryTests
{
    private sealed record ListAssetsQuery : PaginatedQuery;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Clamps_page_to_a_minimum_of_one(int requested, int expected)
    {
        new ListAssetsQuery { Page = requested }.Page.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, PaginatedQuery.DefaultPageSize)]
    [InlineData(-1, PaginatedQuery.DefaultPageSize)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, PaginatedQuery.MaxPageSize)]
    [InlineData(1_000_000, PaginatedQuery.MaxPageSize)]
    public void Clamps_page_size_to_the_configured_ceiling(int requested, int expected)
    {
        // The million-row case is the one that matters. Without the ceiling, `?pageSize=1000000`
        // is an unauthenticated denial-of-service against both the database and the API's memory.
        new ListAssetsQuery { PageSize = requested }.PageSize.ShouldBe(expected);
    }

    [Fact]
    public void Defaults_to_the_first_page_at_the_default_size()
    {
        var query = new ListAssetsQuery();

        query.Page.ShouldBe(1);
        query.PageSize.ShouldBe(PaginatedQuery.DefaultPageSize);
        query.SortDirection.ShouldBe(SortDirection.Ascending);
        query.SearchTerm.ShouldBeNull();
        query.SortBy.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 25, 25)]
    [InlineData(3, 10, 20)]
    public void Computes_skip_from_page_and_page_size(int page, int pageSize, int expectedSkip)
    {
        new ListAssetsQuery { Page = page, PageSize = pageSize }.Skip.ShouldBe(expectedSkip);
    }

    [Fact]
    public void Skip_reflects_the_clamped_page_size_not_the_requested_one()
    {
        // Regression guard: computing Skip from the raw request while taking the clamped size
        // would silently skip rows the client never saw.
        var query = new ListAssetsQuery { Page = 3, PageSize = 500 };

        query.PageSize.ShouldBe(PaginatedQuery.MaxPageSize);
        query.Skip.ShouldBe(200);
    }
}

public sealed class PagedResultTests
{
    [Fact]
    public void Computes_total_pages_by_rounding_up()
    {
        new PagedResult<string>(["a"], page: 1, pageSize: 10, totalCount: 95).TotalPages.ShouldBe(10);
        new PagedResult<string>(["a"], page: 1, pageSize: 10, totalCount: 100).TotalPages.ShouldBe(10);
        new PagedResult<string>(["a"], page: 1, pageSize: 10, totalCount: 101).TotalPages.ShouldBe(11);
    }

    [Fact]
    public void Reports_navigation_availability_at_the_boundaries()
    {
        var first = new PagedResult<string>(["a"], page: 1, pageSize: 10, totalCount: 25);
        first.HasPreviousPage.ShouldBeFalse();
        first.HasNextPage.ShouldBeTrue();

        var last = new PagedResult<string>(["a"], page: 3, pageSize: 10, totalCount: 25);
        last.HasPreviousPage.ShouldBeTrue();
        last.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_page_still_carries_valid_metadata()
    {
        var empty = PagedResult<string>.Empty(page: 2, pageSize: 20);

        empty.Items.ShouldBeEmpty();
        empty.TotalCount.ShouldBe(0);
        empty.TotalPages.ShouldBe(0);
        empty.HasNextPage.ShouldBeFalse();
        empty.Page.ShouldBe(2);
        empty.PageSize.ShouldBe(20);
    }

    [Fact]
    public void A_zero_page_size_does_not_divide_by_zero()
    {
        new PagedResult<string>([], page: 1, pageSize: 0, totalCount: 10).TotalPages.ShouldBe(0);
    }
}

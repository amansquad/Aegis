namespace Aegis.Application.Common.Models;

/// <summary>Sort direction for a list query.</summary>
public enum SortDirection
{
    /// <summary>Ascending order.</summary>
    Ascending = 0,

    /// <summary>Descending order.</summary>
    Descending = 1,
}

/// <summary>
/// Base for any query that returns a page of results, supplying paging, sorting and search.
/// </summary>
/// <remarks>
/// Inherited by every list query so that the contract is identical across modules — clients learn
/// the paging convention once and it holds for assets, incidents, work orders and audit history
/// alike.
/// </remarks>
public abstract record PaginatedQuery
{
    /// <summary>Largest page size a client may request.</summary>
    /// <remarks>
    /// A hard ceiling, not a suggestion. Without it, <c>?pageSize=1000000</c> is an unauthenticated
    /// denial-of-service against both the database and the API's memory — and it will be found,
    /// usually by a well-meaning integrator trying to sync everything in one call.
    /// </remarks>
    public const int MaxPageSize = 100;

    /// <summary>Page size used when the client does not specify one.</summary>
    public const int DefaultPageSize = 25;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    /// <summary>The one-based page number. Values below 1 are clamped to 1.</summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Items per page. Clamped to <see cref="MaxPageSize"/>; non-positive values fall back to
    /// <see cref="DefaultPageSize"/>.
    /// </summary>
    /// <remarks>
    /// Clamped rather than rejected. A client asking for 500 rows wants as many as it can get, and
    /// returning 100 serves that intent; a 400 response for an over-large page teaches integrators
    /// nothing they could not learn from the metadata already in the response.
    /// </remarks>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Free-text search term. Interpretation is query-specific.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Property name to sort by, case-insensitive. Validated against the projection's sortable
    /// properties before use; unknown names are rejected with a 400 rather than ignored.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction. Ignored when <see cref="SortBy"/> is absent.</summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;

    /// <summary>Number of rows to skip, derived from <see cref="Page"/> and <see cref="PageSize"/>.</summary>
    public int Skip => (Page - 1) * PageSize;
}

namespace Aegis.Application.Common.Models;

/// <summary>
/// One page of results together with the metadata a client needs to navigate the rest.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>Creates a page of results.</summary>
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; }

    /// <summary>The one-based page number.</summary>
    public int Page { get; init; }

    /// <summary>The requested page size.</summary>
    public int PageSize { get; init; }

    /// <summary>Total matching items across all pages, before paging is applied.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages available.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when a previous page exists.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>True when a further page exists.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>An empty page, for short-circuit paths that must still return valid metadata.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

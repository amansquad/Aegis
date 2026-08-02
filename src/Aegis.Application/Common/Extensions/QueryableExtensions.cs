using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Aegis.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Common.Extensions;

/// <summary>
/// Composable paging, sorting and conditional-filter helpers for EF Core queries.
/// </summary>
/// <remarks>
/// Every method here composes onto <see cref="IQueryable{T}"/> and defers execution, so filtering,
/// sorting and paging all translate into a single SQL statement. Nothing materialises until a
/// terminal operator runs — which is the property a generic repository typically destroys.
/// </remarks>
public static class QueryableExtensions
{
    private static readonly ConcurrentDictionary<Type, HashSet<string>> SortableCache = new();

    private static readonly MethodInfo OrderByMethod = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2);

    private static readonly MethodInfo OrderByDescendingMethod = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Queryable.OrderByDescending) && m.GetParameters().Length == 2);

    /// <summary>
    /// Applies a predicate only when <paramref name="condition"/> holds.
    /// </summary>
    /// <remarks>
    /// Replaces the imperative <c>if (x is not null) query = query.Where(...)</c> chain that
    /// dominates list handlers, keeping a filter pipeline readable as one expression.
    /// </remarks>
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> source,
        bool condition,
        Expression<Func<T, bool>> predicate) =>
        condition ? source.Where(predicate) : source;

    /// <summary>Applies a predicate only when <paramref name="value"/> is non-null.</summary>
    public static IQueryable<T> WhereIfNotNull<T, TValue>(
        this IQueryable<T> source,
        TValue? value,
        Expression<Func<T, bool>> predicate)
        where TValue : struct =>
        value.HasValue ? source.Where(predicate) : source;

    /// <summary>Applies a predicate only when <paramref name="value"/> is non-blank.</summary>
    public static IQueryable<T> WhereIfNotBlank<T>(
        this IQueryable<T> source,
        string? value,
        Expression<Func<T, bool>> predicate) =>
        string.IsNullOrWhiteSpace(value) ? source : source.Where(predicate);

    /// <summary>
    /// Returns the sortable property names of <typeparamref name="T"/>, case-insensitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by FluentValidation rules so that an unknown <c>sortBy</c> is rejected at the API
    /// boundary with a clear message naming the valid fields, rather than silently ignored — a
    /// client that misspells <c>createdOn</c> should be told, not handed arbitrarily ordered data
    /// it will page through incorrectly.
    /// </para>
    /// <para>
    /// Only scalar properties qualify. Sorting by a navigation property or collection cannot be
    /// translated to SQL and would throw at execution time.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> GetSortableProperties<T>() =>
        SortableCache.GetOrAdd(typeof(T), static type => new HashSet<string>(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSortableType(p.PropertyType))
                .Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase));

    /// <summary>Returns true when <paramref name="propertyName"/> is a sortable property of <typeparamref name="T"/>.</summary>
    public static bool IsSortableProperty<T>(string? propertyName) =>
        !string.IsNullOrWhiteSpace(propertyName)
        && GetSortableProperties<T>().Contains(propertyName);

    /// <summary>
    /// Applies dynamic ordering from a client-supplied property name, falling back to
    /// <paramref name="defaultSort"/> when the name is absent or not sortable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property name is resolved through reflection against <typeparamref name="T"/> and an
    /// expression tree is built from the resolved <see cref="PropertyInfo"/>. The string never
    /// reaches SQL, so this is not an injection vector: an unmatched name cannot become a
    /// fragment of the query, it simply falls back to the default ordering.
    /// </para>
    /// <para>
    /// <b>Always pass a unique-valued <paramref name="defaultSort"/>.</b> SQL Server does not
    /// guarantee a stable order for rows that tie on the sort key, so paging over a non-unique
    /// sort can show the same row on two pages and omit another entirely. Callers sorting by a
    /// non-unique column should add <c>.ThenBy(x =&gt; x.Id)</c> to the result.
    /// </para>
    /// </remarks>
    /// <param name="source">The query to order.</param>
    /// <param name="sortBy">Client-supplied property name, case-insensitive. May be null.</param>
    /// <param name="direction">Sort direction.</param>
    /// <param name="defaultSort">Ordering applied when <paramref name="sortBy"/> cannot be used.</param>
    public static IOrderedQueryable<T> ApplySort<T>(
        this IQueryable<T> source,
        string? sortBy,
        SortDirection direction,
        Expression<Func<T, object?>> defaultSort)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return ApplyDefault(source, direction, defaultSort);
        }

        var property = typeof(T).GetProperty(
            sortBy,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanRead || !IsSortableType(property.PropertyType))
        {
            return ApplyDefault(source, direction, defaultSort);
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        var propertyAccess = Expression.MakeMemberAccess(parameter, property);
        var selector = Expression.Lambda(propertyAccess, parameter);

        var method = direction == SortDirection.Descending
            ? OrderByDescendingMethod
            : OrderByMethod;

        var closedMethod = method.MakeGenericMethod(typeof(T), property.PropertyType);

        return (IOrderedQueryable<T>)closedMethod.Invoke(null, [source, selector])!;
    }

    /// <summary>
    /// Executes the query as a page of results, returning the items and the total match count.
    /// </summary>
    /// <remarks>
    /// Issues two statements: a <c>COUNT</c> over the filtered set, then the page itself. The count
    /// is what lets a client render "page 3 of 47" and is worth its round trip; a cursor-based
    /// alternative avoids it but cannot answer "how many?", which executive dashboards need.
    /// </remarks>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await source.CountAsync(cancellationToken);

        // Skip the second round trip when the filter matched nothing.
        if (totalCount == 0)
        {
            return PagedResult<T>.Empty(page, pageSize);
        }

        var items = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page, pageSize, totalCount);
    }

    /// <summary>Executes a paginated query using the paging fields on <paramref name="query"/>.</summary>
    public static Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        PaginatedQuery query,
        CancellationToken cancellationToken = default) =>
        source.ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);

    /// <summary>
    /// Applies the default ordering, unwrapping the boxing conversion the signature introduces.
    /// </summary>
    /// <remarks>
    /// <c>Expression&lt;Func&lt;T, object?&gt;&gt;</c> lets a caller pass any property regardless of
    /// its type, but the compiler inserts a <c>Convert(..., object)</c> around a value type to
    /// satisfy it. EF Core cannot translate <c>ORDER BY</c> over that boxing node and throws at
    /// execution time, which surfaces as a 500 on the first list endpoint that uses a
    /// <c>DateTimeOffset</c> default sort — as it did here.
    /// </remarks>
    private static IOrderedQueryable<T> ApplyDefault<T>(
        IQueryable<T> source,
        SortDirection direction,
        Expression<Func<T, object?>> defaultSort)
    {
        var method = direction == SortDirection.Descending
            ? OrderByDescendingMethod
            : OrderByMethod;

        if (defaultSort.Body is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            } boxing)
        {
            var unboxed = Expression.Lambda(boxing.Operand, defaultSort.Parameters);
            var closedMethod = method.MakeGenericMethod(typeof(T), boxing.Operand.Type);

            return (IOrderedQueryable<T>)closedMethod.Invoke(null, [source, unboxed])!;
        }

        // Already a reference type, so no conversion was inserted and the expression is usable
        // as-is.
        return direction == SortDirection.Descending
            ? source.OrderByDescending(defaultSort)
            : source.OrderBy(defaultSort);
    }

    /// <summary>
    /// Determines whether a property type can participate in a SQL <c>ORDER BY</c>.
    /// </summary>
    private static bool IsSortableType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid);
    }
}

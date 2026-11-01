namespace OpenSpecification.Paging;

// What every provider's paged query shares: the checks it makes before it sends a query, and the page it applies.
internal static class PagingExtensions
{
    internal static void EnsurePageable<T>(this ISpecification<T> specification, IPagingFilter filter, PaginationSettings settings)
    {
        _ = specification ?? throw new ArgumentNullException(nameof(specification));
        _ = filter ?? throw new ArgumentNullException(nameof(filter));
        _ = settings ?? throw new ArgumentNullException(nameof(settings));

        // The values PaginationEvaluator applies.
        if (specification.Skip > 0 || specification.Take >= 0)
            throw new ArgumentException("A paged query sets Skip and Take from the paging filter. Remove Skip and Take from the specification.", nameof(specification));
    }

    internal static IQueryable<T> ApplyPage<T>(this IQueryable<T> query, Pagination pagination)
    {
        // If skip is 0, avoid adding to the IQueryable, as PaginationEvaluator does.
        if (pagination.Skip > 0)
        {
            query = query.Skip(pagination.Skip);
        }

        return query.Take(pagination.PageSize);
    }
}

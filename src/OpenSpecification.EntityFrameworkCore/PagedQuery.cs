using OpenSpecification.Paging;

namespace OpenSpecification.EntityFrameworkCore;

// Counts, then reads one page, for each paged method of this provider. The caller builds both queries from the
// specification, so a repository's ApplySpecification override shapes both.
internal static class PagedQuery
{
    internal static async Task<PagedResult<TResult>> ToPagedResultAsync<TCount, TResult>(
      IQueryable<TCount> countQuery,
      IQueryable<TResult> pageQuery,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken)
    {
        var count = await countQuery.CountAsync(cancellationToken);
        var pagination = Pagination.Create(count, filter, settings);
        var items = await pageQuery.ApplyPage(pagination).ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, pagination);
    }

    // A projection's total counts the rows its pages hold: one per entity for Select, and the flattened rows for
    // SelectMany.
    internal static Task<PagedResult<TResult>> ToPagedResultAsync<T, TResult>(
      IQueryable<T> criteriaQuery,
      IQueryable<TResult> pageQuery,
      ISpecification<T, TResult> specification,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken)
    {
        return specification.SelectorMany is null
            ? ToPagedResultAsync(criteriaQuery, pageQuery, filter, settings, cancellationToken)
            : ToPagedResultAsync(criteriaQuery.SelectMany(specification.SelectorMany), pageQuery, filter, settings, cancellationToken);
    }
}

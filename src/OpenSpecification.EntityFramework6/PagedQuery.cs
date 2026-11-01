using System.Data.Entity;
using OpenSpecification.Paging;

namespace OpenSpecification.EntityFramework6;

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

    // A projection's total counts the rows its pages hold, one per entity for Select. EF6 can't page a SelectMany: it
    // refuses Skip on rows that aren't ordered, and a specification can order only the entities, never the rows
    // SelectMany flattens out of them. So the method refuses it before any query, on every page, rather than serving
    // page 1 and failing on page 2.
    internal static Task<PagedResult<TResult>> ToPagedResultAsync<T, TResult>(
      IQueryable<T> criteriaQuery,
      IQueryable<TResult> pageQuery,
      ISpecification<T, TResult> specification,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken)
    {
        if (specification.SelectorMany is not null)
            throw new NotSupportedException("EF6 can't page a SelectMany specification, since it can't Skip the flattened rows, which a specification can't order. Use a Select specification, or page the entities.");

        return ToPagedResultAsync(criteriaQuery, pageQuery, filter, settings, cancellationToken);
    }
}

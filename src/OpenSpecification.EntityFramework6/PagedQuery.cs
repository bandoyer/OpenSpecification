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

    // A projection's total counts the rows its pages hold: one per entity for Select, and surviving groups for an
    // ordered GroupBy. EF6 refuses Skip on rows that aren't ordered, so SelectMany, Distinct projections, unordered
    // GroupBy specs, and Distinct GroupBy specs fail fast before any query, on every page, rather than serving page 1
    // and failing on page 2.
    internal static Task<PagedResult<TResult>> ToPagedResultAsync<T, TResult>(
      IQueryable<T> criteriaQuery,
      IQueryable<TResult> pageQuery,
      ISpecification<T, TResult> specification,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken)
    {
        if (GroupByHelper.GetPlan(specification) is { } groupByPlan)
        {
            if (DistinctEvaluator.IsDistinctSpecification(specification))
                throw new NotSupportedException("EF6 can't page a Distinct grouped specification, since it can't Skip distinct projected group rows without ordering them. Evaluate the distinct grouped specification without paging.");

            if (!groupByPlan.HasOrdering)
                throw new NotSupportedException("EF6 can't page an unordered grouped specification, since it requires OrderBy before Skip. Add a group OrderBy to the specification, or evaluate it without paging.");

            return ToPagedResultAsync(groupByPlan.ApplyCount(criteriaQuery), pageQuery, filter, settings, cancellationToken);
        }

        if (specification.SelectorMany is not null)
            throw new NotSupportedException("EF6 can't page a SelectMany specification, since it can't Skip the flattened rows, which a specification can't order. Use a Select specification, or page the entities.");

        if (DistinctEvaluator.IsDistinctSpecification(specification))
            throw new NotSupportedException("EF6 can't page a Distinct projection specification, since it can't Skip distinct projected rows without ordering them. Page the entities, or evaluate the distinct projection without paging.");

        return ToPagedResultAsync(criteriaQuery, pageQuery, filter, settings, cancellationToken);
    }
}

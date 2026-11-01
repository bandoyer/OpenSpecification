using OpenSpecification.Paging;

namespace OpenSpecification.EntityFrameworkCore;

public static class DbSetExtensions
{
    public static async Task<List<TSource>> ToListAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        var result = await SpecificationEvaluator.Default.GetQuery(source, specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? result
            : specification.PostProcessingAction(result).AsList();
    }

    public static async Task<List<TResult>> ToListAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        var result = await SpecificationEvaluator.Default.GetQuery(source, specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? result
            : specification.PostProcessingAction(result).AsList();
    }

    public static async Task<IEnumerable<TSource>> ToEnumerableAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        var result = await SpecificationEvaluator.Default.GetQuery(source, specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? result
            : specification.PostProcessingAction(result);
    }

    public static async Task<IEnumerable<TResult>> ToEnumerableAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        var result = await SpecificationEvaluator.Default.GetQuery(source, specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? result
            : specification.PostProcessingAction(result);
    }

    public static async Task<TSource?> FirstOrDefaultAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification).FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<TResult?> FirstOrDefaultAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification).FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<TSource?> SingleOrDefaultAsync<TSource>(
      this DbSet<TSource> source,
      ISingleResultSpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification).SingleOrDefaultAsync(cancellationToken);
    }

    public static async Task<TResult?> SingleOrDefaultAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISingleResultSpecification<TSource, TResult> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification).SingleOrDefaultAsync(cancellationToken);
    }

    public static async Task<int> CountAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification, evaluateCriteriaOnly: true).CountAsync(cancellationToken);
    }

    public static async Task<bool> AnyAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return await SpecificationEvaluator.Default.GetQuery(source, specification, evaluateCriteriaOnly: true).AnyAsync(cancellationToken);
    }

    public static IAsyncEnumerable<TSource> AsAsyncEnumerable<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification)
      where TSource : class
    {
        return SpecificationEvaluator.Default.GetQuery(source, specification).AsAsyncEnumerable();
    }

    public static IAsyncEnumerable<TResult> AsAsyncEnumerable<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification)
      where TSource : class
    {
        return SpecificationEvaluator.Default.GetQuery(source, specification).AsAsyncEnumerable();
    }

    /// <summary>
    /// Counts the entities that match the <paramref name="specification"/>'s criteria, then returns the page of them that
    /// <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size is clamped to the pages that exist, as
    /// <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does. The page applies the rest of the specification too,
    /// except its PostProcessingAction, which a paged query ignores so the page agrees with the count.
    /// </para>
    /// </summary>
    /// <param name="source">The entities to query.</param>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A task whose result contains the page and its details.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    /// <remarks>The default page size and the page size limit are <see cref="PaginationSettings.Default"/>'s.</remarks>
    public static Task<PagedResult<TSource>> ToPagedListAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      IPagingFilter filter,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return ToPagedListAsync(source, specification, filter, PaginationSettings.Default, cancellationToken);
    }

    /// <summary>
    /// Counts the entities that match the <paramref name="specification"/>'s criteria, then returns the page of them that
    /// <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size is clamped to the pages that exist, as
    /// <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does. The page applies the rest of the specification too,
    /// except its PostProcessingAction, which a paged query ignores so the page agrees with the count.
    /// </para>
    /// </summary>
    /// <param name="source">The entities to query.</param>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="settings">The default page size and the page size limit.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A task whose result contains the page and its details.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/>, <paramref name="filter"/> or <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    public static async Task<PagedResult<TSource>> ToPagedListAsync<TSource>(
      this DbSet<TSource> source,
      ISpecification<TSource> specification,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        specification.EnsurePageable(filter, settings);
        var evaluator = SpecificationEvaluator.Default;
        return await PagedQuery.ToPagedResultAsync(evaluator.GetQuery(source, specification, evaluateCriteriaOnly: true), evaluator.GetQuery(source, specification), filter, settings, cancellationToken);
    }

    /// <summary>
    /// Counts the results that match the <paramref name="specification"/>'s criteria, then returns the page of its
    /// projections that <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size is clamped to the pages that exist, as
    /// <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does. For a SelectMany, it counts the flattened results,
    /// which are what the pages hold. The page applies the rest of the specification too,
    /// except its PostProcessingAction, which a paged query ignores so the page agrees with the count.
    /// </para>
    /// </summary>
    /// <typeparam name="TSource">The type of the entities.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by the projection.</typeparam>
    /// <param name="source">The entities to query.</param>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A task whose result contains the page and its details.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    /// <remarks>The default page size and the page size limit are <see cref="PaginationSettings.Default"/>'s.</remarks>
    public static Task<PagedResult<TResult>> ToPagedListAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification,
      IPagingFilter filter,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        return ToPagedListAsync(source, specification, filter, PaginationSettings.Default, cancellationToken);
    }

    /// <summary>
    /// Counts the results that match the <paramref name="specification"/>'s criteria, then returns the page of its
    /// projections that <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size is clamped to the pages that exist, as
    /// <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does. For a SelectMany, it counts the flattened results,
    /// which are what the pages hold. The page applies the rest of the specification too,
    /// except its PostProcessingAction, which a paged query ignores so the page agrees with the count.
    /// </para>
    /// </summary>
    /// <typeparam name="TSource">The type of the entities.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by the projection.</typeparam>
    /// <param name="source">The entities to query.</param>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="settings">The default page size and the page size limit.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A task whose result contains the page and its details.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/>, <paramref name="filter"/> or <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    public static async Task<PagedResult<TResult>> ToPagedListAsync<TSource, TResult>(
      this DbSet<TSource> source,
      ISpecification<TSource, TResult> specification,
      IPagingFilter filter,
      PaginationSettings settings,
      CancellationToken cancellationToken = default)
      where TSource : class
    {
        specification.EnsurePageable(filter, settings);
        var evaluator = SpecificationEvaluator.Default;
        return await PagedQuery.ToPagedResultAsync(evaluator.GetQuery(source, specification, evaluateCriteriaOnly: true), evaluator.GetQuery(source, specification), specification, filter, settings, cancellationToken);
    }

    public static IQueryable<TSource> WithSpecification<TSource>(
      this IQueryable<TSource> source,
      ISpecification<TSource> specification,
      ISpecificationEvaluator? evaluator = null)
      where TSource : class
    {
        evaluator ??= SpecificationEvaluator.Default;
        return evaluator.GetQuery(source, specification);
    }

    public static IQueryable<TResult> WithSpecification<TSource, TResult>(
      this IQueryable<TSource> source,
      ISpecification<TSource, TResult> specification,
      ISpecificationEvaluator? evaluator = null)
      where TSource : class
    {
        evaluator ??= SpecificationEvaluator.Default;
        return evaluator.GetQuery(source, specification);
    }
}

using OpenSpecification.Paging;

namespace OpenSpecification.EntityFrameworkCore;

/// <inheritdoc/>
public abstract class RepositoryBase<T> : IRepositoryBase<T> where T : class
{
    protected DbContext DbContext { get; set; }
    protected ISpecificationEvaluator SpecificationEvaluator { get; set; }

    public RepositoryBase(DbContext dbContext)
        : this(dbContext, EntityFrameworkCore.SpecificationEvaluator.Default)
    {
    }

    /// <inheritdoc/>
    public RepositoryBase(DbContext dbContext, ISpecificationEvaluator specificationEvaluator)
    {
        DbContext = dbContext;
        SpecificationEvaluator = specificationEvaluator;
    }

    /// <inheritdoc/>
    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Add(entity);

        await SaveChangesAsync(cancellationToken);

        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().AddRange(entities);

        await SaveChangesAsync(cancellationToken);

        return entities;
    }

    /// <inheritdoc/>
    public virtual async Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Update(entity);

        var result = await SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public virtual async Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().UpdateRange(entities);

        var result = await SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public virtual async Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Remove(entity);

        var result = await SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public virtual async Task<int> DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().RemoveRange(entities);

        var result = await SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public virtual async Task<int> DeleteRangeAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        var query = ApplySpecification(specification);
        DbContext.Set<T>().RemoveRange(query);

        var result = await SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull
    {
        return await DbContext.Set<T>().FindAsync(new object[] { id }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<List<T>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        var queryResult = await ApplySpecification(specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? queryResult
            : specification.PostProcessingAction(queryResult).AsList();
    }

    /// <inheritdoc/>
    public virtual async Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        var queryResult = await ApplySpecification(specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? queryResult
            : specification.PostProcessingAction(queryResult).AsList();
    }

    /// <inheritdoc/>
    public virtual async Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, true).CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, true).AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Counts the entities that match the <paramref name="specification"/>'s criteria, then returns the page of them that
    /// <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size is clamped to the
    /// pages that exist, as <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does. The page applies the rest of the specification too,
    /// except its PostProcessingAction, which a paged query ignores so the page agrees with the count.
    /// </para>
    /// </summary>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the page and its details.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    /// <remarks>The default page size and the page size limit are <see cref="PaginationSettings"/>'s.</remarks>
    public virtual async Task<PagedResult<T>> PagedListAsync(ISpecification<T> specification, IPagingFilter filter, CancellationToken cancellationToken = default)
    {
        specification.EnsurePageable(filter, PaginationSettings);
        return await PagedQuery.ToPagedResultAsync(ApplySpecification(specification, true), ApplySpecification(specification), filter, PaginationSettings, cancellationToken);
    }

    /// <summary>
    /// Counts the results that match the <paramref name="specification"/>'s criteria, then returns the page of its
    /// projections that <paramref name="filter"/> asks for, with the page's details. An out-of-range page or page size
    /// is clamped to the pages that exist, as <see cref="Pagination.Create"/> says.
    /// <para>
    /// The count evaluates the criteria only, as CountAsync does, and counts one result per entity for a Select. For a
    /// SelectMany, it counts the flattened results, which are what the pages hold. The page applies the rest of the
    /// specification too, except its PostProcessingAction, which a paged query ignores so the page agrees with the
    /// count.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The type of the value returned by the projection.</typeparam>
    /// <param name="specification">The encapsulated query logic. It can't have Skip or Take, since the filter sets the page.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the page and its details.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="specification"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="specification"/> has Skip or Take.</exception>
    /// <remarks>The default page size and the page size limit are <see cref="PaginationSettings"/>'s.</remarks>
    public virtual async Task<PagedResult<TResult>> PagedListAsync<TResult>(ISpecification<T, TResult> specification, IPagingFilter filter, CancellationToken cancellationToken = default)
    {
        specification.EnsurePageable(filter, PaginationSettings);
        return await PagedQuery.ToPagedResultAsync(ApplySpecification(specification, true), ApplySpecification(specification), specification, filter, PaginationSettings, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> specification)
    {
        return ApplySpecification(specification).AsAsyncEnumerable();
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(ISpecification<T, TResult> specification)
    {
        return ApplySpecification(specification).AsAsyncEnumerable();
    }

    /// <summary>
    /// Gets the default page size and the page size limit that PagedListAsync uses. Override it to give this
    /// repository its own. The default is <see cref="PaginationSettings.Default"/>.
    /// </summary>
    protected virtual PaginationSettings PaginationSettings => PaginationSettings.Default;

    /// <summary>
    /// Filters the entities  of <typeparamref name="T"/>, to those that match the encapsulated query logic of the
    /// <paramref name="specification"/>.
    /// </summary>
    /// <param name="specification">The encapsulated query logic.</param>
    /// <param name="evaluateCriteriaOnly">It ignores pagination and evaluators that don't affect Count.</param>
    /// <returns>The filtered entities as an <see cref="IQueryable{T}"/>.</returns>
    protected virtual IQueryable<T> ApplySpecification(ISpecification<T> specification, bool evaluateCriteriaOnly = false)
    {
        return SpecificationEvaluator.GetQuery(DbContext.Set<T>().AsQueryable(), specification, evaluateCriteriaOnly);
    }

    /// <summary>
    /// Filters all entities of <typeparamref name="T" />, that matches the encapsulated query logic of the
    /// <paramref name="specification"/>, from the database.
    /// <para>
    /// Projects each entity into a new form, being <typeparamref name="TResult" />.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The type of the value returned by the projection.</typeparam>
    /// <param name="specification">The encapsulated query logic.</param>
    /// <returns>The filtered projected entities as an <see cref="IQueryable{T}"/>.</returns>
    protected virtual IQueryable<TResult> ApplySpecification<TResult>(ISpecification<T, TResult> specification)
    {
        return SpecificationEvaluator.GetQuery(DbContext.Set<T>().AsQueryable(), specification);
    }
}

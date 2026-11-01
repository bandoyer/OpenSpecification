using System.Runtime.CompilerServices;
using OpenSpecification.Paging;

namespace OpenSpecification.EntityFrameworkCore;

public abstract class ContextFactoryRepositoryBaseOfT<TEntity, TContext> : IRepositoryBase<TEntity>
  where TEntity : class
  where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly ISpecificationEvaluator _specificationEvaluator;

    public ContextFactoryRepositoryBaseOfT(IDbContextFactory<TContext> dbContextFactory)
      : this(dbContextFactory, SpecificationEvaluator.Default)
    {
    }

    public ContextFactoryRepositoryBaseOfT(IDbContextFactory<TContext> dbContextFactory,
      ISpecificationEvaluator specificationEvaluator)
    {
        _dbContextFactory = dbContextFactory;
        _specificationEvaluator = specificationEvaluator;
    }

    /// <inheritdoc/>
    public async Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await dbContext.Set<TEntity>().FindAsync(new object[] { id }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification,
      CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await dbContext.Set<TEntity>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var queryResult = await ApplySpecification(specification, dbContext).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? queryResult
            : specification.PostProcessingAction(queryResult).AsList();
    }

    /// <inheritdoc/>
    public async Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var queryResult = await ApplySpecification(specification, dbContext).ToListAsync(cancellationToken);

        return specification.PostProcessingAction is null
            ? queryResult
            : specification.PostProcessingAction(queryResult).AsList();
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext, true).CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await dbContext.Set<TEntity>().CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await ApplySpecification(specification, dbContext, true).AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await dbContext.Set<TEntity>().AnyAsync(cancellationToken);
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
    public async Task<PagedResult<TEntity>> PagedListAsync(ISpecification<TEntity> specification, IPagingFilter filter, CancellationToken cancellationToken = default)
    {
        specification.EnsurePageable(filter, PaginationSettings);
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await PagedQuery.ToPagedResultAsync(ApplySpecification(specification, dbContext, true), ApplySpecification(specification, dbContext), filter, PaginationSettings, cancellationToken);
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
    public async Task<PagedResult<TResult>> PagedListAsync<TResult>(ISpecification<TEntity, TResult> specification, IPagingFilter filter, CancellationToken cancellationToken = default)
    {
        specification.EnsurePageable(filter, PaginationSettings);
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return await PagedQuery.ToPagedResultAsync(ApplySpecification(specification, dbContext, true), ApplySpecification(specification, dbContext), specification, filter, PaginationSettings, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TEntity> AsAsyncEnumerable(ISpecification<TEntity> specification)
    {
        // Here, since the iterator below runs nothing until the caller enumerates.
        ArgumentNullException.ThrowIfNull(specification);
        return AsAsyncEnumerableCore(dbContext => ApplySpecification(specification, dbContext), default);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(ISpecification<TEntity, TResult> specification)
    {
        // Here, since the iterator below runs nothing until the caller enumerates.
        ArgumentNullException.ThrowIfNull(specification);
        return AsAsyncEnumerableCore(dbContext => ApplySpecification(specification, dbContext), default);
    }

    // An async iterator, so each enumeration creates its own context and disposes it when the enumeration ends, the
    // caller stops early, or the token from WithCancellation is cancelled. The query runs once the context exists.
    private async IAsyncEnumerable<TResult> AsAsyncEnumerableCore<TResult>(Func<TContext, IQueryable<TResult>> query,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        await foreach (var item in query(dbContext).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    /// <inheritdoc/>
    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().Add(entity);

        await SaveChangesAsync(dbContext, cancellationToken);

        return entity;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().AddRange(entities);

        await SaveChangesAsync(dbContext, cancellationToken);

        return entities;
    }

    /// <inheritdoc/>
    public async Task<int> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().Update(entity);

        var result = await SaveChangesAsync(dbContext, cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().UpdateRange(entities);

        var result = await SaveChangesAsync(dbContext, cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().Remove(entity);

        var result = await SaveChangesAsync(dbContext, cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<TEntity>().RemoveRange(entities);

        var result = await SaveChangesAsync(dbContext, cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteRangeAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var query = ApplySpecification(specification, dbContext);
        dbContext.Set<TEntity>().RemoveRange(query);

        var result = await SaveChangesAsync(dbContext, cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException();
    }

    public async Task<int> SaveChangesAsync(TContext dbContext, CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the default page size and the page size limit that PagedListAsync uses. Override it to give this
    /// repository its own. The default is <see cref="PaginationSettings.Default"/>.
    /// </summary>
    protected virtual PaginationSettings PaginationSettings => PaginationSettings.Default;

    /// <summary>
    /// Filters the entities  of <typeparamref name="TEntity"/>, to those that match the encapsulated query logic of the
    /// <paramref name="specification"/>.
    /// </summary>
    /// <param name="specification">The encapsulated query logic.</param>
    /// <param name="dbContext">The DbContext instance.</param>
    /// <param name="evaluateCriteriaOnly">It ignores pagination and evaluators that don't affect Count.</param>
    /// <returns>The filtered entities as an <see cref="IQueryable{T}"/>.</returns>
    protected virtual IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification, TContext dbContext, bool evaluateCriteriaOnly = false)
    {
        return _specificationEvaluator.GetQuery(dbContext.Set<TEntity>().AsQueryable(), specification, evaluateCriteriaOnly);
    }

    /// <summary>
    /// Filters all entities of <typeparamref name="TEntity" />, that matches the encapsulated query logic of the
    /// <paramref name="specification"/>, from the database.
    /// <para>
    /// Projects each entity into a new form, being <typeparamref name="TResult" />.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The type of the value returned by the projection.</typeparam>
    /// <param name="specification">The encapsulated query logic.</param>
    /// <param name="dbContext">The DbContext instance.</param>
    /// <returns>The filtered projected entities as an <see cref="IQueryable{T}"/>.</returns>
    protected virtual IQueryable<TResult> ApplySpecification<TResult>(ISpecification<TEntity, TResult> specification, TContext dbContext)
    {
        return _specificationEvaluator.GetQuery(dbContext.Set<TEntity>().AsQueryable(), specification);
    }
}

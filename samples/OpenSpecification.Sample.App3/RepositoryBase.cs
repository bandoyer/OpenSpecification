using OpenSpecification.Sample.Domain.Filters;
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using OpenSpecification.Paging;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace OpenSpecification.Sample.App3;

public abstract class RepositoryBase<T> : IReadRepository<T> where T : class
{
    protected DbContext DbContext { get; set; }
    protected readonly AutoMapper.IConfigurationProvider _configurationProvider;
    protected ISpecificationEvaluator Evaluator { get; }

    // We have a custom evaluator for QueryTag, therefore we're passing our custom specification evaluator
    protected RepositoryBase(DbContext dbContext, IMapper mapper)
        : this(dbContext, AppSpecificationEvaluator.Instance, mapper)
    {
    }

    protected RepositoryBase(DbContext dbContext, ISpecificationEvaluator specificationEvaluator, IMapper mapper)
    {
        DbContext = dbContext;
        Evaluator = specificationEvaluator;
        _configurationProvider = mapper.ConfigurationProvider;
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Add(entity);

        await SaveChangesAsync(cancellationToken);

        return entity;
    }
    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().AddRange(entities);

        await SaveChangesAsync(cancellationToken);

        return entities;
    }
    public virtual async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Update(entity);

        await SaveChangesAsync(cancellationToken);
    }
    public virtual async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Remove(entity);

        await SaveChangesAsync(cancellationToken);
    }
    public virtual async Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().RemoveRange(entities);

        await SaveChangesAsync(cancellationToken);
    }
    public virtual async Task DeleteRangeAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        var query = ApplySpecification(specification);
        DbContext.Set<T>().RemoveRange(query);

        await SaveChangesAsync(cancellationToken);
    }
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task<T?> FindAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull
    {
        return await DbContext.Set<T>().FindAsync(new object[] { id }, cancellationToken: cancellationToken);
    }
    public async Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<T?> SingleOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).SingleOrDefaultAsync(cancellationToken);
    }
    public async Task<TResult?> SingleOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).SingleOrDefaultAsync(cancellationToken);
    }
    public virtual async Task<List<T>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().ToListAsync(cancellationToken);
    }
    public virtual async Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        var queryResult = await ApplySpecification(specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction == null ? queryResult : specification.PostProcessingAction(queryResult).ToList();
    }
    public virtual async Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        var queryResult = await ApplySpecification(specification).ToListAsync(cancellationToken);

        return specification.PostProcessingAction == null ? queryResult : specification.PostProcessingAction(queryResult).ToList();
    }
    public virtual async Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, true).CountAsync(cancellationToken);
    }
    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().CountAsync(cancellationToken);
    }
    public virtual async Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification, true).AnyAsync(cancellationToken);
    }
    public virtual async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<T>().AnyAsync(cancellationToken);
    }

    public async Task<TResult?> ProjectToFirstOrDefaultAsync<TResult>(ISpecification<T> specification, CancellationToken cancellationToken)
    {
        return await ApplySpecification(specification)
            .ProjectTo<TResult>(_configurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<TResult>> ProjectToListAsync<TResult>(ISpecification<T> specification, CancellationToken cancellationToken)
    {
        return await ApplySpecification(specification)
            .ProjectTo<TResult>(_configurationProvider)
            .ToListAsync(cancellationToken);
    }

    // The library's PagedListAsync pages a specification's own projection. This pages an AutoMapper projection, so it
    // counts and pages by hand, with the library's paging types.
    public async Task<PagedResult<TResult>> ProjectToListAsync<TResult>(ISpecification<T> specification, BaseFilter filter, CancellationToken cancellationToken)
    {
        var count = await ApplySpecification(specification, true).CountAsync(cancellationToken);
        var pagination = Pagination.Create(count, filter, PaginationSettings.Default);

        var data = await ApplySpecification(specification)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ProjectTo<TResult>(_configurationProvider)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(data, pagination);
    }

    protected virtual IQueryable<T> ApplySpecification(ISpecification<T> specification, bool evaluateCriteriaOnly = false)
    {
        return Evaluator.GetQuery(DbContext.Set<T>(), specification, evaluateCriteriaOnly);
    }
    protected virtual IQueryable<TResult> ApplySpecification<TResult>(ISpecification<T, TResult> specification)
    {
        return Evaluator.GetQuery(DbContext.Set<T>(), specification);
    }
}

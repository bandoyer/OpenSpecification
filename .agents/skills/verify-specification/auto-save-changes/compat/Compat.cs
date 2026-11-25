// Compile-only compatibility check for #74: a class implementing IRepositoryBase<T> directly and a RepositoryBase<T>
// subclass overriding write methods with their merge-base signatures must build cleanly on both the merge base and branch.
using OpenSpecification;

namespace CompatCheck;

public sealed class CompatEntity
{
    public int Id { get; set; }
}

public sealed class HandImplementedRepository : IRepositoryBase<CompatEntity>
{
    public Task<CompatEntity> AddAsync(CompatEntity entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
    public Task<IEnumerable<CompatEntity>> AddRangeAsync(IEnumerable<CompatEntity> entities, CancellationToken cancellationToken = default) => Task.FromResult(entities);
    public Task<int> UpdateAsync(CompatEntity entity, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> UpdateRangeAsync(IEnumerable<CompatEntity> entities, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> DeleteAsync(CompatEntity entity, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> DeleteRangeAsync(IEnumerable<CompatEntity> entities, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> DeleteRangeAsync(ISpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<CompatEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => Task.FromResult<CompatEntity?>(null);
    public Task<CompatEntity?> FirstOrDefaultAsync(ISpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult<CompatEntity?>(null);
    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<CompatEntity, TResult> specification, CancellationToken cancellationToken = default) => Task.FromResult<TResult?>(default);
    public Task<CompatEntity?> SingleOrDefaultAsync(ISingleResultSpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult<CompatEntity?>(null);
    public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<CompatEntity, TResult> specification, CancellationToken cancellationToken = default) => Task.FromResult<TResult?>(default);
    public Task<List<CompatEntity>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<CompatEntity>());
    public Task<List<CompatEntity>> ListAsync(ISpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult(new List<CompatEntity>());
    public Task<List<TResult>> ListAsync<TResult>(ISpecification<CompatEntity, TResult> specification, CancellationToken cancellationToken = default) => Task.FromResult(new List<TResult>());
    public Task<int> CountAsync(ISpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<bool> AnyAsync(ISpecification<CompatEntity> specification, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public IAsyncEnumerable<CompatEntity> AsAsyncEnumerable(ISpecification<CompatEntity> specification) => throw new NotImplementedException();
    public IAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(ISpecification<CompatEntity, TResult> specification) => throw new NotImplementedException();
}

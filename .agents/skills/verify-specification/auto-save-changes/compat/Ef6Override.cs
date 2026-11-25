// Compile-only subclass override check for #74 on EF6's RepositoryBase<T>.
using System.Data.Entity;
using OpenSpecification.EntityFramework6;

namespace CompatCheck;

public sealed class Ef6OverrideRepository(DbContext dbContext) : RepositoryBase<CompatEntity>(dbContext)
{
    public override Task<CompatEntity> AddAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.AddAsync(entity, cancellationToken);

    public override Task<int> UpdateAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.UpdateAsync(entity, cancellationToken);

    public override Task<int> DeleteAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.DeleteAsync(entity, cancellationToken);
}

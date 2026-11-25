// Compile-only subclass override check for #74 on EF Core's RepositoryBase<T>.
using Microsoft.EntityFrameworkCore;
using OpenSpecification.EntityFrameworkCore;

namespace CompatCheck;

public sealed class EfCoreOverrideRepository(DbContext dbContext) : RepositoryBase<CompatEntity>(dbContext)
{
    public override Task<CompatEntity> AddAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.AddAsync(entity, cancellationToken);

    public override Task<int> UpdateAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.UpdateAsync(entity, cancellationToken);

    public override Task<int> DeleteAsync(CompatEntity entity, CancellationToken cancellationToken = default)
        => base.DeleteAsync(entity, cancellationToken);
}

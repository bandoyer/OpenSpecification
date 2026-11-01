---
layout: default
title: PostProcessingAction
nav_order: 8
has_children: false
parent: Base Features
grand_parent: Features
---

# PostProcessingAction

The `PostProcessingAction` allows you to define a delegate of type `Func<IEnumerable<T>, IEnumerable<T>>` that is applied after data is retrieved from an external source (e.g., a database). This enables in-memory transformations or filtering that cannot be handled by the underlying query provider.

The delegate itself is stored as part of the specification state, but it is executed within the repository or data access layer after the initial query completes.

```csharp
public class CompanySpec : Specification<Company>
{
    public CompanySpec(int countryId)
    {
        Query.Where(x => x.CountryId == countryId)
             .Include(x => x.Stores);

        Query.PostProcessingAction(companies =>
        {
            // Your custom in-memory operation on the result set.
            return companies;
        });
    }
}
```

Repository usage.

```csharp
public virtual async Task<List<T>> ListAsync(
    ISpecification<T> spec, 
    CancellationToken cancellationToken = default)
{
    var result = await SpecificationEvaluator.Default
        .GetQuery(_dbContext.Heroes, spec)
        .ToListAsync(cancellationToken);

    return spec.PostProcessingAction is null 
        ? result 
        : spec.PostProcessingAction(result).ToList();
}
```

## Methods that apply it

Only the methods that return a list apply `PostProcessingAction`. Each one passes the rows the query returns to the delegate.

- `ListAsync` on `RepositoryBase<T>` in both providers, and on the EF Core provider's `ContextFactoryRepositoryBase<TEntity, TContext>`.
- `ToListAsync` and `ToEnumerableAsync` on a `DbSet<T>`, from each provider's `DbSetExtensions`.
- `InMemorySpecificationEvaluator.Evaluate`, which `Specification<T>.Evaluate` calls.

These methods ignore it, on the repositories and on a `DbSet<T>`:

- `CountAsync`
- `AnyAsync`
- `FirstOrDefaultAsync`
- `SingleOrDefaultAsync`
- `AsAsyncEnumerable`, which only the EF Core provider implements. The EF6 provider's `RepositoryBase<T>` throws `NotImplementedException` from it.
- `PagedListAsync` on the repositories, and `ToPagedListAsync` on a `DbSet<T>`, so the page agrees with its count. See [How to get a page of results](../usage/use-paged-results.md).

`CountAsync` and `AnyAsync` evaluate only the specification's criteria, such as `Where` and `Search`, and run in SQL. They skip `Include`, `OrderBy`, `Skip` and `Take`, and they load no rows for `PostProcessingAction` to run on. So a filter in `PostProcessingAction` makes `ListAsync` and `CountAsync` disagree. `ListAsync` applies `Skip` and `Take` in SQL, and then the delegate removes rows from the page it loaded. `CountAsync` still counts the removed rows, so a page count built from it is too high, as [issue #18](https://github.com/bandoyer/OpenSpecification/issues/18) found when paging.

A filter that a page count must include goes in `Where`, so the database applies it.

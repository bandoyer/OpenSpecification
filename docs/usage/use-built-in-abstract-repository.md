---
layout: default
title: How to use the Built In Abstract Repository
parent: Usage
nav_order: 4
---

# How to use the Built In Abstract Repository

This library primarily provides facilities for creating and evaluating specifications. However, because specifications are often used alongside the repository pattern, it also includes base repository implementations.

It's important to note that application requirements can vary widely, so this library does not attempt to offer a one-size-fits-all solution. Instead, the base implementations are designed to serve as reference points or starting templates for your own customized repositories.

Define your `IRepository<>` and `Repository<>` by inheriting from the base constructs as shown below.

```csharp
public interface IRepository<T> : IRepositoryBase<T> where T : class
{
    // Define additional contracts if necessary.
}

public class Repository<T> : RepositoryBase<T>, IRepository<T> where T : class
{
    public Repository(SampleDbContext dbContext) : base(dbContext)
    {
    }

    // Implement additional behavior if necessary.
    // All base methods are marked as virtual and can be overloaded.
}
```

Then register the repository in your dependency injection container.

```csharp
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
```

That’s it! You can now inject `IRepository<>` into your services and use it to query your data. The base repository exposes methods with canonical naming conventions that should be self-explanatory.

### Example usage.

```csharp
public class CustomerByLastNameSpec : Specification<Customer>
{
    public CustomerByLastNameSpec(string lastName)
    {
        Query.Where(c => c.LastName == lastName)
             .Include(x => x.Addresses)
             .OrderBy(x => x.FirstName);
    }
}
```

```csharp
app.MapGet("/customers/{lastName}", 
    async (IRepository<Customer> customerRepo, string lastName, CancellationToken cancellationToken) =>
{
    var spec = new CustomerByLastNameSpec(lastName);
    var customers = await customerRepo.ListAsync(spec, cancellationToken);
    return customers;
});
```

### SingleAsync and SingleOrDefaultAsync methods

These repository methods accepts an `ISingleResultSpecification<T>` as a parameter. This feature was requested by the community to avoid accidental misusages. By creating a `SingleResultSpecification`, it hints to be more careful and think whether the defined conditions should yield a single result. All other methods, `FirstAsync`, `FirstOrDefaultAsync`, `ListAsync`, etc., do not have this constraint and accept an `ISpecification<T>` parameter.

```csharp
public class CustomerByIdSpec : SingleResultSpecification<Customer>
{
    public CustomerByIdSpec(int id)
    {
        Query.Where(c => c.Id == id);
    }
}
```

```csharp
var spec = new CustomerByIdSpec(101);
var customer = await customerRepo.SingleAsync(spec, cancellationToken);
```


### CountAsync and AnyAsync methods

The `ISpecificationEvaluator.GetQuery()` method accepts an additional optional `bool evaluateCriteriaOnly = false` parameter. If set to `true` it will ignore the `Take`, `Skip`, `OrderBy` and `Include` conditions. For paginated results, commonly we'd like to retrieve items in a given page, but also the total number of items. The `CountAsync` repository method evaluates the specification in this special mode, therefore we can reuse the same specification and avoid having unnecessary duplicates (one with pagination and another one without it).

```csharp
public class CustomerSpec : Specification<Customer>
{
    public CustomerSpec(string lastName, int skip, int take)
    {
        Query.Where(c => c.LastName == lastName)
             .Include(x => x.Addresses)
             .OrderBy(x => x.FirstName)
             .Skip(skip)
             .Take(take);
    }
}
```

```csharp
var spec = new CustomerSpec("Smith", 10, 10);
var totalItems = await customerRepo.CountAsync(spec, cancellationToken);
var customers = await customerRepo.ListAsync(spec, cancellationToken);
```

### Deferring `SaveChangesAsync` (Unit of Work)

By default, every write method on `RepositoryBase<T>` (`AddAsync`, `AddRangeAsync`, `UpdateAsync`, `UpdateRangeAsync`, `DeleteAsync`, and both `DeleteRangeAsync` overloads) calls `SaveChangesAsync` immediately. To batch multiple writes across one or more repositories sharing a scoped `DbContext` into a single atomic Unit of Work, override `AutoSaveChanges` to return `false` in your repository subclass (in either the EF Core or EF6 provider):

```csharp
public class Repository<T> : RepositoryBase<T>, IRepository<T> where T : class
{
    public Repository(SampleDbContext dbContext) : base(dbContext)
    {
    }

    protected override bool AutoSaveChanges => false;
}
```

With `AutoSaveChanges` set to `false`, write methods stage changes on the shared `DbContext` without calling `SaveChangesAsync`: `AddAsync` and `AddRangeAsync` still return the entity or entities, while `UpdateAsync`, `UpdateRangeAsync`, `DeleteAsync`, and both `DeleteRangeAsync` overloads return `0` (since no state entries have been written to the database yet). Commit the Unit of Work with a single `SaveChangesAsync` call:

```csharp
await orderRepo.AddAsync(order, cancellationToken);
await customerRepo.UpdateAsync(customer, cancellationToken);

// Commits both writes atomically on the shared DbContext.
// If any write fails, none of the staged changes are persisted.
await orderRepo.SaveChangesAsync(cancellationToken);
```

`ContextFactoryRepositoryBaseOfT<TEntity, TContext>` does not have `AutoSaveChanges`, because each of its write methods creates and disposes its own `DbContext`.

## Resources

An in-depth demo of a similar implementation of the Repository Pattern and `RepositoryBase<T>` can be found in the Repositories section of this [Pluralsight course](https://www.pluralsight.com/courses/domain-driven-design-fundamentals).

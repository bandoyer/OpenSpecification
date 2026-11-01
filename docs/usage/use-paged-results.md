---
layout: default
title: How to get a page of results
parent: Usage
nav_order: 7
---

# How to get a page of results

A web API that lists rows usually returns one page of them with the page's details: the total count, the number of pages, and whether a page comes before or after. Both providers' repository bases have `PagedListAsync`, and their `DbSet` extensions have `ToPagedListAsync`, which do this in one call. Each counts the rows that match the specification's criteria, then reads the page the filter asks for.

The paging types are in the `OpenSpecification.Paging` namespace, in the core package:

- `IPagingFilter` and `PagingFilter`: the page and page size the caller asks for, both optional.
- `PaginationSettings`: the page size to use when the filter gives none, and the largest page size allowed. `PaginationSettings.Default` is 10 and 100.
- `Pagination`: the page's details, `Page`, `PageSize`, `TotalItems`, `TotalPages`, `HasPrevious` and `HasNext`.
- `PagedResult<T>`: the page's `Items` and its `Pagination`.

They're in a namespace of their own so they don't clash with paging classes of your own named `Pagination` or `PaginationSettings`. A file that calls `PagedListAsync` and reads its result through `var` needs no using directive for them.

## Through a repository

Give the specification its criteria and an order. Paging without an `OrderBy` returns rows in whatever order the database picks, which can change from one page to the next. On EF6, a page past the first fails without an order, since EF6 refuses `Skip` on rows that aren't sorted.

```csharp
public class HeroesByNameSpec : Specification<Hero>
{
    public HeroesByNameSpec(string? superPower)
    {
        Query.Where(h => h.SuperPower == superPower, superPower is not null)
             .OrderBy(h => h.Name);
    }
}
```

Then call `PagedListAsync` with a filter.

```csharp
var page = await repository.PagedListAsync(new HeroesByNameSpec("flight"), new PagingFilter { Page = 2, PageSize = 20 });

// page.Items holds heroes 21 to 40.
// page.Pagination holds Page 2, PageSize 20, TotalItems, TotalPages, HasPrevious and HasNext.
```

`IReadRepositoryBase<T>` doesn't declare `PagedListAsync`, so a repository you inject through an interface of your own, as [How to use the Built In Abstract Repository](./use-built-in-abstract-repository.md) shows, doesn't get it from there. Declare it on your interface, and the provider's base class implements it:

```csharp
public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class
{
    Task<PagedResult<T>> PagedListAsync(ISpecification<T> specification, IPagingFilter filter, CancellationToken cancellationToken = default);
}
```

To give a repository its own page sizes, override `PaginationSettings`:

```csharp
public class EfRepository<T>(AppDbContext dbContext) : RepositoryBase<T>(dbContext), IReadRepository<T> where T : class
{
    protected override PaginationSettings PaginationSettings { get; } = new(defaultPageSize: 25, pageSizeLimit: 200);
}
```

## Through a DbSet

`ToPagedListAsync` takes the same filter, and optionally the settings:

```csharp
var page = await dbContext.Heroes.ToPagedListAsync(new HeroesByNameSpec("flight"), filter);
var small = await dbContext.Heroes.ToPagedListAsync(new HeroesByNameSpec("flight"), filter, new PaginationSettings(5, 20));
```

## A filter from a web request

Implement `IPagingFilter` on the class or record your endpoint binds its query string to:

```csharp
public record HeroFilter(int? Page, int? PageSize, string? SuperPower) : IPagingFilter;

app.MapGet("/heroes", async ([AsParameters] HeroFilter filter, IReadRepository<Hero> repository) =>
    await repository.PagedListAsync(new HeroesByNameSpec(filter.SuperPower), filter));
```

System.Text.Json writes the result as `{"items":[...],"pagination":{"page":2,"pageSize":20,"totalItems":57,"totalPages":3,"hasPrevious":true,"hasNext":true}}`, and reads it back into a `PagedResult<T>`. The types carry no serializer attributes.

## Out-of-range filters

A page or page size out of range is clamped, not refused:

- A missing, zero or negative page is page 1, and a page past the last is the last page.
- A missing, zero or negative page size is the settings' default, and a page size past the limit is the limit.
- A query that matches nothing returns one empty page: page 1 of 1, with `TotalItems` 0.

`Pagination.Create(totalItems, filter, settings)` applies the same rules, for a query you count and page yourself.

## Projections

Both methods have an overload for a specification with a `Select`, which returns a page of the projected values. The count is one per entity.

With a `SelectMany`, the EF Core provider counts the flattened results, since those are what the pages hold. The EF6 provider can't page a `SelectMany` specification: EF6 refuses `Skip` on rows that aren't ordered, and a specification can order only the entities, not the rows `SelectMany` flattens out of them. So on EF6 both methods throw `NotSupportedException` for a `SelectMany` specification, before any query. Use a `Select` instead.

## What a paged query leaves out

- The count evaluates the criteria only, such as `Where` and `Search`, as `CountAsync` does. The page query applies the rest of the specification too, such as `Include`, `OrderBy` and `AsNoTracking`.
- `PostProcessingAction` is ignored, so the page agrees with the count. See [PostProcessingAction](../features/postprocessingaction.md).
- A specification with its own `Skip` or `Take` throws `ArgumentException`, since the filter sets the page.
- The count and the page are two queries. A row added or removed between them shows in the page but not in the count.

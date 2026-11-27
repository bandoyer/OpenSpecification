# Distinct specifications

Since #98, `Query.Distinct()` and `Query.Distinct(bool condition)` deduplicate results on both entity specifications (`Specification<T>`) and `Select`/`SelectMany` projection specifications (`Specification<T, TResult>`) across In-Memory, EF Core, and EF6 without modifying `ISpecification<T>`, `ISpecification<T, TResult>`, or `IReadRepositoryBase<T>`.

## Sub-features

- `contract-surface`: `IDistinctSpecification` (`bool IsDistinct`), `Specification<T>.IsDistinct`, and `DistinctEvaluator` (`IsCriteriaEvaluator == true`) are public, while `ISpecification<T>`, `ISpecification<T, TResult>`, and `IReadRepositoryBase<T>` remain unchanged.
- `inmemory-value-types`: `Distinct()` and `Distinct(bool condition)` run unconstrained on `Specification<int>` in memory (`Distinct(false)` leaves duplicates intact and does not clear an earlier `Distinct(true)`).
- `entity-distinct`: Entity `Distinct()` runs after `Where`/`Search` and before `Include`/`OrderBy`/`Skip`/`Take` across `ListAsync`, `CountAsync`, `AnyAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AsAsyncEnumerable` (EF Core), `WithSpecification`, `PagedListAsync`, and `ToPagedListAsync`.
- `projection-select-distinct`: Projection `Select` + `Distinct()` deduplicates projected values, applies `Skip`/`Take` after `Distinct()` (omitting `Skip(0)` on `IQueryable`), and applies `PostProcessingAction` last on list-returning methods only.
- `projection-selectmany-distinct`: Projection `SelectMany` + `Distinct()` deduplicates flattened projected values, applies `Skip`/`Take` after `Distinct()`, and applies `PostProcessingAction` last on list-returning methods.
- `conflict-orderby-distinct`: Combining entity `OrderBy`/`OrderByDescending` with projection `Distinct()` throws `NotSupportedException` across In-Memory, EF Core, and EF6 without sending SQL.
- `upcast-criteria-only`: Upcast `CountAsync`/`AnyAsync((ISpecification<T>)distinctProjectionSpec)` counts entities without emitting `SELECT DISTINCT` in SQL, `IsSatisfiedBy` checks entity predicates only, and `WithProjectionOf` sets `IsDistinct` from `projectionSpec.IsDistinct`.
- `paged-distinct-select` / `paged-distinct-selectmany` (EF Core): `PagedListAsync` / `ToPagedListAsync` counts and pages distinct `Select` and `SelectMany` projections (including past one page, clamping out-of-range pages, preserving custom `ApplySpecification` overrides, ignoring `PostProcessingAction`, and rejecting `Skip`/`Take` with `ArgumentException` before sending SQL).
- `ef6-paged-distinct-refused` (EF6): `PagedListAsync` / `ToPagedListAsync` rejects distinct `Select` and `SelectMany` projections with `NotSupportedException` up front on page 1 and page 2 without sending SQL.

## How to get to it (user POV)

- Call `Query.Distinct()` or `Query.Distinct(condition)` on a `Specification<T>` or `Specification<T, TResult>`, and run it through `RepositoryBase<T>`, `ContextFactoryRepositoryBaseOfT<TEntity, TContext>`, `DbSetExtensions`, `WithSpecification`, or `Specification<T>.Evaluate`.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #98, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/distinct`.

- **EF Core driver.** Make an EF Core consumer, replace its `.cs` files with `"$S"/efcore/*.cs`, then build and run:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-distinct-efcore")
  rm "$C"/*.cs && cp "$S"/efcore/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Expected lines:

  | Label | Expected result |
  |---|---|
  | `contract-surface` | `iface=True; prop=True; evalPublic=True; evalCriteria=True; untouched=True` |
  | `inmemory-value-types` | `distinct=[1,2,3]; condFalse=[1,1,2]; sticky=[1,2]` |
  | `entity-distinct` | `list=[P02(Alpha),P03(Alpha)]; count=9; any=True; first=P02; single=P01; stream=[P02,P03]; withSpecCount=2; repoPage=[P07]/3; dbSetPage=[P01,P02]/3` |
  | `projection-select-distinct` | `repoCount=2; postPrefix=True; dbSetMatch=True; firstNoPost=True; single=Cat-1; streamNoPost=True; skipZero=[Cat-1,Cat-2,Cat-3,Cat-4]; skipZeroHasOffset=False; withSpecCount=4; memCount=2` |
  | `projection-selectmany-distinct` | `all=[Tag-A,Tag-B,Tag-C,Tag-D,Tag-E]; slicedCount=2; slicedPost=True; withSpecCount=5; memAll=[Tag-A,Tag-B,Tag-C,Tag-D,Tag-E]; memSlicedCount=2` |
  | `conflict-orderby-distinct` | `mem=True; repoSelect=True; repoSelectMany=True; dbSet=True; sqlCommands=0` |
  | `upcast-criteria-only` | `count=6; any=True; sqlHasDistinct=False; isSatisfiedBy=True; withProjFalseToTrue=True; withProjTrueToFalse=False` |
  | `paged-distinct-select` | `p1=2/6/tp3; p2=2/p2; p99=p3/2; factoryTotal=6; dbSetTotal=6; tenantTotal=4/tp2; noPost=True; skipRejected=True; skipSql=0` |
  | `paged-distinct-selectmany` | `p1=2/5/tp3; p2=2; p3=1/hasNext=False; factoryTotal=5; dbSetP3=1/5` |

- **EF6 driver.** Same with `verify consumer` and `"$S"/ef6/*.cs`:

  ```bash
  C=$(verify consumer "$T" "$(basename "$T")-distinct-ef6")
  rm "$C"/*.cs && cp "$S"/ef6/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Expected lines:

  | Label | Expected result |
  |---|---|
  | `contract-surface` | `iface=True; prop=True; evalPublic=True; evalCriteria=True; untouched=True` |
  | `inmemory-value-types` | `distinct=[1,2,3]; condFalse=[1,1,2]; sticky=[1,2]` |
  | `entity-distinct` | `list=[P02(Alpha),P03(Alpha)]; count=9; any=True; first=P02; single=P01; withSpecCount=2; repoPage=[P07]/3; dbSetPage=[P01,P02]/3` |
  | `projection-select-distinct` | `all=[Cat-1,Cat-2,Cat-3,Cat-4]; repoCount=2; postPrefix=True; dbSetMatch=True; firstNoPost=True; single=Cat-1; skipZeroHasRowNumber=False; withSpecCount=4; memCount=2` |
  | `projection-selectmany-distinct` | `all=[#Tag-A,#Tag-B,#Tag-C,#Tag-D,#Tag-E]; takenCount=2; withSpecCount=5; memAll=[#Tag-A,#Tag-B,#Tag-C,#Tag-D,#Tag-E]; memTakenCount=2` |
  | `conflict-orderby-distinct` | `mem=True; repoSelect=True; repoSelectMany=True; dbSet=True; sqlCommands=0` |
  | `upcast-criteria-only` | `count=6; any=True; sqlHasDistinct=False; isSatisfiedBy=True; withProjFalseToTrue=True; withProjTrueToFalse=False` |
  | `ef6-paged-distinct-refused` | `repoSelectP1=True; repoSelectP2=True; dbSetSelectP1=True; repoSelectManyP1=True; dbSetSelectManyP2=True; sqlCommands=0` |

- **Existing queries.** Drive the default EF Core and EF6 consumers on `$B` and `$T` and `diff` each pair of `verify run` outputs, checking both hold rows.

## Gotchas

- Remove the template's `.cs` files before copying `efcore/DistinctDriver.cs` or `ef6/DistinctDriver.cs` so there is only one top-level statements file (avoids CS8802).
- Each driver creates and drops its own SQL Server database (`VerifySpecificationDistinctEFCore` or `VerifySpecificationDistinctEF6`).
- EF6 requires sorted input for `Skip(n > 0)`, and a distinct projection cannot carry entity `OrderBy`, so the EF6 projection driver exercises `Skip(0)` omission and `Take` rather than `Skip(1)` on `IQueryable`, and verifies that EF6 refuses paged distinct projections up front on every page.

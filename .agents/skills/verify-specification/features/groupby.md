# Grouped projections (GroupBy)

Since #99, `Query.GroupBy(keySelector).Select(g => ...)` on `Specification<T, TResult>` executes grouped aggregate projections across In-Memory, EF Core, and EF6 via an isolated fluent staged builder (`IGroupedSpecificationBuilder<T, TKey, TResult>` and `IGroupedResultSpecificationBuilder<T, TResult>`) without modifying `ISpecification<T>`, `ISpecification<T, TResult>`, or `IReadRepositoryBase<T>`.

## Sub-features

- `contract-surface`: `IGroupedSpecificationBuilder<T, TKey, TResult>`, `IGroupedResultSpecificationBuilder<T, TResult>`, and `DuplicateGroupByException` are public and isolated from `ISpecificationBuilder<T, TResult>`; `ISpecification<T>`, `ISpecification<T, TResult>`, `IReadRepositoryBase<T>`, and `void` `Select`/`SelectMany` are unchanged.
- `compile-time-isolation`: `Query.GroupBy(...)` fails to compile on `Specification<T>`, entity clauses (`Where`, `Include`, `OrderBy(T)`, `Select(T)`) fail to compile on `IGroupedSpecificationBuilder`, and a second `.Select(...)` fails to compile on `IGroupedResultSpecificationBuilder`.
- `groupby-single-and-composite`: single-key, anonymous composite-key, and member-initialized record/DTO composite-key projections with `Count`, `Sum`, `Min`, `Max`, `Average`, pre-group `Where`/`Search`/EF query-shaping flags, and empty input sequences (including constant keys) execute across In-Memory, EF Core, and EF6 via `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AsAsyncEnumerable` (EF Core), `WithSpecification`, `ToListAsync`, `ToEnumerableAsync`, and `Evaluate`.
- `postprocessing-and-cache`: `.PostProcessingAction(...)` runs on list-returning methods only; `.EnableCache(...)` and `.WithCacheKey(...)` (with and without `condition`) set `CacheKey`/`CacheEnabled` and reject null/empty active keys with `ArgumentException`.
- `validation-and-conflicts`: second `GroupBy` throws `DuplicateGroupByException` at builder time without mutating the first plan; evaluation throws `SelectorNotFoundException` (missing `.Select`), `ConcurrentSelectorsException` (`GroupBy` mixed with `Select`/`SelectMany`), `NotSupportedException` (pre-group entity `OrderBy`, entity `Skip`/`Take`, and interim `Query.Distinct()` / `PagedListAsync` / `ToPagedListAsync` guards) with 0 SQL commands; pre-group `Include`/`IncludeString` are ignored.
- `composition-and-custom-evaluator`: `WithProjectionOf(groupedSpec)` clones `GroupByPlan` without state leakage, ignores `source` includes, and fails fast at evaluation time if `source` carries entity `OrderBy` or `Skip`/`Take`; custom `SpecificationEvaluator` and `InMemorySpecificationEvaluator` subclasses inherit `GroupBy` support; `IsSatisfiedBy(T)` and upcast `CountAsync`/`AnyAsync((ISpecification<T>)groupedSpec)` remain entity-criteria-only.

## How to get to it (user POV)

- Call `Query.GroupBy(x => ...).Select(g => ...)` (optionally chaining `.PostProcessingAction(...)`, `.EnableCache(...)`, or `.WithCacheKey(...)`) on a `Specification<T, TResult>` or `SingleResultSpecification<T, TResult>`, and run it through `RepositoryBase<T>`, `ContextFactoryRepositoryBaseOfT<TEntity, TContext>`, `DbSetExtensions`, `WithSpecification`, or `Evaluate`.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #99, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/groupby`.

- **EF Core driver.** Make an EF Core consumer, replace its `.cs` files with `"$S"/efcore/*.cs`, then build and run:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-groupby-efcore")
  rm "$C"/*.cs && cp "$S"/efcore/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Expected lines:

  | Label | Expected result |
  |---|---|
  | `contract-surface` | `grouped=True; result=True; dupEx=True; isolated=True; ifacesUntouched=True; selectVoid=True` |
  | `groupby-single-and-composite` | `single=[Cat-1:3/90/10/50/30,Cat-2:4/200/20/80/50]; surfacesMatch=True; tag=True; ignoreFilter=[Cat-1:3/140,Cat-2:2/60]; tracking=[Cat-1:1/50,Cat-2:2/140]; first=Cat-1:50; singleRes=Cat-1:50; composite=[Alpha/Cat-1:2/40,Alpha/Cat-2:2/60,Beta/Cat-1:1/50,Beta/Cat-2:2/140]; compositeMatch=True; empty=0/0/0/0` |
  | `postprocessing-and-cache` | `list=POST:Cat-1; dbSetList=POST:Cat-1; dbSetEnum=POST:Cat-1; mem=POST:Cat-1; first=Cat-1; single=Cat-1; stream=Cat-1; cache=GroupedCat-Beta-1/True; withKey=CustomKey; condFalse=False; emptyThrows=True` |
  | `validation-and-conflicts` | `dup=True/True; missingSelect=True; concurrent=True; preOrder=True; prePaging=True; interimDistinct=True; interimPaged=True; conflictSql=0; includeIgnored=True` |
  | `composition-and-custom-evaluator` | `composed=[Cat-1,Cat-2]; noLeak=True; composedConflicts=True; customEf=[Cat-1,Cat-2]; customMem=[Cat-1,Cat-2]; upcast=4/True/hasGroupBy=False; isSatisfiedBy=True` |

- **EF6 driver.** Same with `verify consumer` and `"$S"/ef6/*.cs`:

  ```bash
  C=$(verify consumer "$T" "$(basename "$T")-groupby-ef6")
  rm "$C"/*.cs && cp "$S"/ef6/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Expected lines:

  | Label | Expected result |
  |---|---|
  | `contract-surface` | `grouped=True; result=True; dupEx=True; isolated=True; ifacesUntouched=True; selectVoid=True` |
  | `groupby-single-and-composite` | `single=[Cat-1:3/90/10/50/30,Cat-2:4/200/20/80/50]; surfacesMatch=True; filtered=[Cat-1:2/40,Cat-2:2/60]; first=Cat-1:50; singleRes=Cat-1:50; composite=[Alpha/Cat-1:2/40,Alpha/Cat-2:2/60,Beta/Cat-1:1/50,Beta/Cat-2:2/140]; compositeMatch=True; empty=0/0/0/0` |
  | `postprocessing-and-cache` | `list=POST:Cat-1; dbSetList=POST:Cat-1; dbSetEnum=POST:Cat-1; mem=POST:Cat-1; first=Cat-1; single=Cat-1; cache=GroupedCat-Beta-1/True; withKey=CustomKey; condFalse=False; emptyThrows=True` |
  | `validation-and-conflicts` | `dup=True/True; missingSelect=True; concurrent=True; preOrder=True; prePaging=True; interimDistinct=True; interimPaged=True; conflictSql=0; includeIgnored=True` |
  | `composition-and-custom-evaluator` | `composed=[Cat-1,Cat-2]; noLeak=True; composedConflicts=True; customEf=[Cat-1,Cat-2]; customMem=[Cat-1,Cat-2]; upcast=4/True/hasGroupBy=False; isSatisfiedBy=True` |

- **Compile-time isolation snippet.** Copy `"$S"/compile-fail/GroupByCompileChecks.cs` into a dedicated consumer and build it:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-groupby-compile-fail")
  cp "$S"/compile-fail/GroupByCompileChecks.cs "$C/"
  verify build "$C" 2>&1
  ```

  Expect `build: exit 1` and a compiler error on all six marked lines (`GROUPBY_ON_ENTITY`, `WHERE_AFTER_GROUPBY`, `INCLUDE_AFTER_GROUPBY`, `ORDERBY_ENTITY_AFTER_GROUPBY`, `SELECT_ENTITY_AFTER_GROUPBY`, `SECOND_SELECT_AFTER_RESULT`).

- **Existing queries.** Drive the default EF Core and EF6 consumers on `$B` and `$T` and `diff` each pair of `verify run` outputs, checking both hold rows.

## Gotchas

- Remove the template's `.cs` files before copying `efcore/GroupByDriver.cs` or `ef6/GroupByDriver.cs` so there is only one top-level statements file (avoids CS8802).
- Each driver creates and drops its own SQL Server database (`VerifySpecificationGroupByEFCore` or `VerifySpecificationGroupByEF6`).
- Put `GroupByCompileChecks.cs` in its own consumer so its expected compile errors do not block the runnable drivers.

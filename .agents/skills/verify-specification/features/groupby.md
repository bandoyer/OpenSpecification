# Grouped projections (GroupBy)

Since #99 and #100, `Query.GroupBy(keySelector)` on `Specification<T, TResult>` executes grouped aggregate projections with optional `Having(...)` filtering and strongly typed post-group `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` ordering across In-Memory, EF Core, and EF6 via isolated fluent staged builders (`IGroupedSpecificationBuilder<T, TKey, TResult>`, `IOrderedGroupedSpecificationBuilder<T, TKey, TResult>`, and `IGroupedResultSpecificationBuilder<T, TResult>`) without modifying `ISpecification<T>`, `ISpecification<T, TResult>`, or `IReadRepositoryBase<T>`.

## Sub-features

- `contract-surface`: `IGroupedSpecificationBuilder<T, TKey, TResult>`, `IOrderedGroupedSpecificationBuilder<T, TKey, TResult>` (inheriting `IGroupedSpecificationBuilder<T, TKey, TResult>`), `IGroupedResultSpecificationBuilder<T, TResult>`, and `DuplicateGroupByException` are public and isolated from `ISpecificationBuilder<T, TResult>`; `ISpecification<T>`, `ISpecification<T, TResult>`, `IReadRepositoryBase<T>`, and `void` `Select`/`SelectMany` are unchanged.
- `compile-time-isolation`: `Query.GroupBy(...)` fails to compile on `Specification<T>`, entity clauses (`Where`, `Include`, `OrderBy(T)`, `Select(T)`) fail to compile on `IGroupedSpecificationBuilder` and `IOrderedGroupedSpecificationBuilder`, `ThenBy`/`ThenByDescending` fail to compile without `OrderBy`/`OrderByDescending` or after `.Having(...)`, and a second `.Select(...)` fails to compile on `IGroupedResultSpecificationBuilder`.
- `groupby-single-and-composite`: single-key, anonymous composite-key, and member-initialized record/DTO composite-key projections with `Count`, `Sum`, `Min`, `Max`, `Average`, pre-group `Where`/`Search`/EF query-shaping flags, and empty input sequences (including constant keys) execute across In-Memory, EF Core, and EF6 via `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AsAsyncEnumerable` (EF Core), `WithSpecification`, `ToListAsync`, `ToEnumerableAsync`, and `Evaluate`.
- `having-and-ordering`: multiple `Having` predicates combine with AND semantics (before or after group `OrderBy`, with `Having(..., false)` as a no-op); strongly typed `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` (including conditional overloads) sort by group key, composite key members, and unprojected value-type aggregates (`Count`, `Sum`, `Max`, `Average`) with zero boxing exceptions across In-Memory, EF Core, and EF6; per-builder `IsChainDiscarded` isolates discarded chains and nested `ThenBy(..., false)` chains while keeping saved handles active; two active primary order chains throw `DuplicateOrderChainException` with 0 SQL; null active arguments throw `ArgumentNullException`; and `WithProjectionOf` clones `Having` and order steps without state leakage.
- `postprocessing-and-cache`: `.PostProcessingAction(...)` runs on list-returning methods only; `.EnableCache(...)` and `.WithCacheKey(...)` (with and without `condition`) set `CacheKey`/`CacheEnabled` and reject null/empty active keys with `ArgumentException`.
- `validation-and-conflicts`: second `GroupBy` throws `DuplicateGroupByException` at builder time without mutating the first plan; evaluation throws `SelectorNotFoundException` (missing `.Select`), `ConcurrentSelectorsException` (`GroupBy` mixed with `Select`/`SelectMany`), `NotSupportedException` (pre-group entity `OrderBy`, entity `Skip`/`Take`, and interim `Query.Distinct()` / `PagedListAsync` / `ToPagedListAsync` guards) with 0 SQL commands; pre-group `Include`/`IncludeString` are ignored.
- `composition-and-custom-evaluator`: `WithProjectionOf(groupedSpec)` clones `GroupByPlan` without state leakage, ignores `source` includes, and fails fast at evaluation time if `source` carries entity `OrderBy` or `Skip`/`Take`; custom `SpecificationEvaluator` and `InMemorySpecificationEvaluator` subclasses inherit `GroupBy` support; `IsSatisfiedBy(T)` and upcast `CountAsync`/`AnyAsync((ISpecification<T>)groupedSpec)` remain entity-criteria-only.

## How to get to it (user POV)

- Call `Query.GroupBy(x => ...)` followed by optional `.Having(...)`, `.OrderBy(...)/.OrderByDescending(...)`, `.ThenBy(...)/.ThenByDescending(...)`, and `.Select(g => ...)` (plus optional `.PostProcessingAction(...)`, `.EnableCache(...)`, or `.WithCacheKey(...)`) on a `Specification<T, TResult>` or `SingleResultSpecification<T, TResult>`, and run it through `RepositoryBase<T>`, `ContextFactoryRepositoryBaseOfT<TEntity, TContext>`, `DbSetExtensions`, `WithSpecification`, or `Evaluate`.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #100, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/groupby`.

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
  | `contract-surface` | `grouped=True; ordered=True/True; result=True; dupEx=True; isolated=True; ifacesUntouched=True; selectVoid=True` |
  | `groupby-single-and-composite` | `single=[Cat-1:3/90/10/50/30,Cat-2:4/200/20/80/50]; surfacesMatch=True; tag=True; ignoreFilter=[Cat-1:3/140,Cat-2:2/60]; tracking=[Cat-1:1/50,Cat-2:2/140]; first=Cat-1:50; singleRes=Cat-1:50; composite=[Alpha/Cat-1:2/40,Alpha/Cat-2:2/60,Beta/Cat-1:1/50,Beta/Cat-2:2/140]; compositeMatch=True; empty=0/0/0/0` |
  | `having-and-ordering` | `having=[Beta/Cat-2:2/140,Alpha/Cat-2:2/60]; havingMatch=True; aggOrder=[Alpha/Cat-2,Alpha/Cat-1,Beta/Cat-2,Beta/Cat-1]; aggSurfacesMatch=True; avg=[Alpha/Cat-1,Alpha/Cat-2,Beta/Cat-1,Beta/Cat-2]; max=[Beta/Cat-2,Beta/Cat-1,Alpha/Cat-2,Alpha/Cat-1]; avgMaxMatch=True; discardedPrimary=[Beta/Cat-2,Alpha/Cat-2,Beta/Cat-1,Alpha/Cat-1]; nestedDiscard=[Alpha/Cat-1,Alpha/Cat-2,Beta/Cat-2,Beta/Cat-1]; chainIsolationMatch=True; empty=0/0; dupOrder=True/sql=0; oneActive=[Cat-1,Cat-2]; nullGuards=True/inactiveCount=2; cloneNoLeak=True` |
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
  | `contract-surface` | `grouped=True; ordered=True/True; result=True; dupEx=True; isolated=True; ifacesUntouched=True; selectVoid=True` |
  | `groupby-single-and-composite` | `single=[Cat-1:3/90/10/50/30,Cat-2:4/200/20/80/50]; surfacesMatch=True; filtered=[Cat-1:2/40,Cat-2:2/60]; first=Cat-1:50; singleRes=Cat-1:50; composite=[Alpha/Cat-1:2/40,Alpha/Cat-2:2/60,Beta/Cat-1:1/50,Beta/Cat-2:2/140]; compositeMatch=True; empty=0/0/0/0` |
  | `having-and-ordering` | `having=[Beta/Cat-2:2/140,Alpha/Cat-2:2/60]; havingMatch=True; aggOrder=[Alpha/Cat-2,Alpha/Cat-1,Beta/Cat-2,Beta/Cat-1]; aggSurfacesMatch=True; avg=[Alpha/Cat-1,Alpha/Cat-2,Beta/Cat-1,Beta/Cat-2]; max=[Beta/Cat-2,Beta/Cat-1,Alpha/Cat-2,Alpha/Cat-1]; avgMaxMatch=True; discardedPrimary=[Beta/Cat-2,Alpha/Cat-2,Beta/Cat-1,Alpha/Cat-1]; nestedDiscard=[Alpha/Cat-1,Alpha/Cat-2,Beta/Cat-2,Beta/Cat-1]; chainIsolationMatch=True; empty=0/0; dupOrder=True/sql=0; oneActive=[Cat-1,Cat-2]; nullGuards=True/inactiveCount=2; cloneNoLeak=True` |
  | `postprocessing-and-cache` | `list=POST:Cat-1; dbSetList=POST:Cat-1; dbSetEnum=POST:Cat-1; mem=POST:Cat-1; first=Cat-1; single=Cat-1; cache=GroupedCat-Beta-1/True; withKey=CustomKey; condFalse=False; emptyThrows=True` |
  | `validation-and-conflicts` | `dup=True/True; missingSelect=True; concurrent=True; preOrder=True; prePaging=True; interimDistinct=True; interimPaged=True; conflictSql=0; includeIgnored=True` |
  | `composition-and-custom-evaluator` | `composed=[Cat-1,Cat-2]; noLeak=True; composedConflicts=True; customEf=[Cat-1,Cat-2]; customMem=[Cat-1,Cat-2]; upcast=4/True/hasGroupBy=False; isSatisfiedBy=True` |

- **Compile-time isolation snippet.** Copy `"$S"/compile-fail/GroupByCompileChecks.cs` into a dedicated consumer and build it:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-groupby-compile-fail")
  cp "$S"/compile-fail/GroupByCompileChecks.cs "$C/"
  verify build "$C" 2>&1
  ```

  Expect `build: exit 1` and a compiler error on all thirteen marked lines (`GROUPBY_ON_ENTITY`, `WHERE_AFTER_GROUPBY`, `INCLUDE_AFTER_GROUPBY`, `ORDERBY_ENTITY_AFTER_GROUPBY`, `SELECT_ENTITY_AFTER_GROUPBY`, `THENBY_WITHOUT_ORDERBY`, `THENBYDESC_WITHOUT_ORDERBY`, `WHERE_AFTER_ORDERED_GROUPBY`, `INCLUDE_AFTER_ORDERED_GROUPBY`, `SELECT_ENTITY_AFTER_ORDERED_GROUPBY`, `THENBY_AFTER_HAVING`, `THENBYDESC_AFTER_HAVING`, `SECOND_SELECT_AFTER_RESULT`).

- **Existing queries.** Drive the default EF Core and EF6 consumers on `$B` and `$T` and `diff` each pair of `verify run` outputs, checking both hold rows.

## Gotchas

- Remove the template's `.cs` files before copying `efcore/GroupByDriver.cs` or `ef6/GroupByDriver.cs` so there is only one top-level statements file (avoids CS8802).
- Each driver creates and drops its own SQL Server database (`VerifySpecificationGroupByEFCore` or `VerifySpecificationGroupByEF6`).
- Put `GroupByCompileChecks.cs` in its own consumer so its expected compile errors do not block the runnable drivers.

# EF Core, EF6, and In-Memory Translation Constraints for `Distinct()` and `GroupBy()`

Research report for [`bandoyer/OpenSpecification#89`](https://github.com/bandoyer/OpenSpecification/issues/89) (parent map [`#88`](https://github.com/bandoyer/OpenSpecification/issues/88); informs `#90`, `#91`, and `#92`).

> **Evidence boundary:** Sections 1–2 report **empirically executed** queries run against SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest` via `spec-sql`) using the exact package versions pinned in `Directory.Packages.props` (`Microsoft.EntityFrameworkCore.SqlServer` **10.0.14**, `EntityFramework` **6.5.1**) and .NET 10 LINQ-to-Objects. Section 3 is **static codebase analysis** of `HEAD` (`224e4a5`). Section 4 summarizes **upstream `ardalis/Specification#138` context** from public issue references.

---

## 1. `Distinct()` Behavior Across EF Core 10, EF6, and LINQ-to-Objects

### 1.1 Empirical Matrix

| Scenario | EF Core 10.0.14 (SQL Server) | EF6 6.5.1 (SQL Server) | LINQ-to-Objects |
|---|---|---|---|
| Entity `Where(...).Distinct()` | **OK** — emits `SELECT DISTINCT [s].[Id], ...` | **OK** — elides `DISTINCT` (PK guarantees row uniqueness) | **OK** — uses `EqualityComparer<T>.Default` |
| `Select(navEntity).Distinct()` (e.g., `s => s.Company`) | **OK** — `SELECT DISTINCT [c].[Id], [c].[Name] FROM [Stores] INNER JOIN [Companies] ...` | **OK** — `SELECT DISTINCT [Extent2].[Id], [Extent2].[Name] ...` | **OK** — reference/value equality on `Company` |
| `Select(scalar).Distinct()` | **OK** — `SELECT DISTINCT [s].[City] FROM [Stores]` (groups `NULL` into one row) | **OK** — `SELECT DISTINCT [Extent1].[City] ...` (groups `NULL` into one row) | **OK** — `NULL` deduplicated to one entry |
| `Select(new Dto { ... }).Distinct()` (plain class, no `Equals`) | **OK** — `SELECT DISTINCT [s].[City], [s].[CompanyId]` (6 rows) | **OK** — `SELECT DISTINCT [Extent1].[City], [Extent1].[CompanyId], 1 AS [C1]` (6 rows) | **DIVERGENCE** — reference equality returns all **7** rows unless `Equals`/`GetHashCode` overridden or `record`/struct used |
| `Select(new PosRecord(a, b)).Distinct().ToListAsync()` | **OK** — `SELECT DISTINCT [s].[City], [s].[CompanyId]` (6 rows) | **FAIL** — `NotSupportedException: Only parameterless constructors and initializers are supported in LINQ to Entities.` | **OK** — value equality (6 rows) |
| `Select(new PosRecord(a, b)).Distinct().CountAsync()` | **FAIL** — `InvalidOperationException: Operation is not valid due to the current state of the object.` | **FAIL** — same parameterless-ctor error | **OK** |
| `Select(new PosRecord(a, b)).Distinct().OrderBy(r => r.City)` | **FAIL** — `InvalidOperationException: The LINQ expression '...Distinct().OrderBy(e => e.City)' could not be translated.` | **FAIL** — same parameterless-ctor error | **OK** |
| `Select(new PosRecord(a, b)).Distinct().AnyAsync()` | **OK** | **FAIL** — same parameterless-ctor error | **OK** |
| `Select(new InitRecord { ... }).Distinct().CountAsync()` | **OK** (4 rows) | **OK** (`SELECT COUNT(1) FROM (SELECT DISTINCT ...)`) (4 rows) | **OK** |
| `Select(new { ... }).Distinct().CountAsync()` | **OK** | **OK** | **OK** |
| `Select(scalar).Distinct().CountAsync()` | **OK** | **OK** | **OK** |
| `SelectMany(c => c.Stores.Select(s => s.City)).Distinct()` | **OK** — `SELECT DISTINCT [s].[City] FROM [Companies] INNER JOIN [Stores] ...` | **OK** — `SELECT DISTINCT [Extent1].[City] FROM [dbo].[Stores]` | **OK** |
| `SelectMany(...).Distinct().CountAsync()` | **OK** | **OK** — `SELECT COUNT(1) FROM (SELECT DISTINCT ...)` | **OK** |
| `OrderBy(s => s.City).Distinct()` (entity) | **DROPS `ORDER BY`** — `SELECT DISTINCT ... FROM [Stores]` | **DROPS `ORDER BY`** — `SELECT ... FROM [Stores]` | **DIVERGENCE** — preserves encounter order (ordered) |
| `OrderBy(s => s.Name).Select(s => s.City).Distinct()` (sort col not in `Select`) | **DROPS `ORDER BY`** — `SELECT DISTINCT [s].[City]` | **DROPS `ORDER BY`** — `SELECT DISTINCT [Extent1].[City]` | **DIVERGENCE** — ordered by `Name`, then first-seen distinct `City` |
| `OrderBy(s => s.City).Select(s => s.City).Distinct()` (sort col in `Select`) | **DROPS `ORDER BY`** — `SELECT DISTINCT [s].[City]` | **DROPS `ORDER BY`** — `SELECT DISTINCT [Extent1].[City]` | **DIVERGENCE** — ordered by `City` |
| `OrderBy(s => s.City).Select(s => s.City).Distinct().Skip(1).Take(2)` | **UNORDERED PAGING** — `SELECT [s0].[City] FROM (SELECT DISTINCT ...) ORDER BY (SELECT 1) OFFSET @p FETCH NEXT @p1` | **FAIL** — `NotSupportedException: The method 'Skip' is only supported for sorted input in LINQ to Entities. The method 'OrderBy' must be called before the method 'Skip'.` | **OK** — ordered slice |
| `OrderBy(s => s.City).Select(s => s.City).Distinct().Take(2)` (page 1, `Skip == 0`) | **UNORDERED** — `SELECT DISTINCT TOP(@p)` | **SUCCEEDS UNORDERED** — `SELECT DISTINCT TOP (2)` (page 1 works, page 2 crashes!) | **OK** |
| `Select(s => s.City).Distinct().OrderBy(c => c).Skip(1).Take(2)` | **OK** — `SELECT [s0].[City] FROM (SELECT DISTINCT ...) ORDER BY [s0].[City] OFFSET @p FETCH NEXT @p1` | **OK** — `SELECT ... FROM (SELECT DISTINCT ...) ORDER BY row_number() OVER (ORDER BY [Distinct1].[City] ASC) OFFSET 1 FETCH NEXT 2` | **OK** |
| `Select(dto).Distinct().OrderBy(d => d.City).Skip(1).Take(2)` | **OK** (member-init class/record) | **OK** (member-init class/record) | **OK** |
| `OrderBy(s => s.Name).Skip(1).Take(3).Select(s => s.City).Distinct()` | **PAGINATES BEFORE DISTINCT** — `SELECT DISTINCT [s0].[City] FROM (SELECT ... OFFSET 1 FETCH NEXT 3)` (can return `< Take` rows!) | **PAGINATES BEFORE DISTINCT** — same inner `OFFSET/FETCH` then outer `DISTINCT` | **PAGINATES BEFORE DISTINCT** |
| `Include(c => c.Stores).Distinct()` (collection include) | **OK** — `SELECT ... FROM (SELECT DISTINCT [c].[Id], [c].[Name] FROM [Companies]) LEFT JOIN [Stores] ... ORDER BY [c0].[Id]` | **OK** — elides `DISTINCT`, loads collection | N/A |
| `Include(c => c.Stores).AsSplitQuery().Distinct()` | **OK** — split query with `(SELECT DISTINCT [c].[Id], [c].[Name] ...)` | N/A (EF Core only) | N/A |
| `Include(c => c.Stores).Distinct().OrderBy(c => c.Id).Skip(1).Take(2)` | **OK** (both single and `AsSplitQuery`) | **OK** | N/A |
| `AsNoTracking().Distinct()` / `AsNoTrackingWithIdentityResolution().Include(...).Distinct()` | **OK** — 0 tracked entries; identity resolution preserved (`ReferenceEquals == true`) | **OK** (`AsNoTracking` — 0 tracked entries) | N/A |
| Empty source `Where(false).Select(s => s.City).Distinct()` (`CountAsync`/`AnyAsync`/`FirstOrDefaultAsync`) | **OK** — `0`, `false`, `null` | **OK** — `0`, `false`, `null` | **OK** |

### 1.2 Decisive Constraints for `Distinct()` Design (`#90`)

1. **Both EF Core 10 and EF6 silently drop `OrderBy` placed before `Distinct()`.**
   - Control check confirmed: without `Distinct()`, EF6 preserves `OrderBy(s => s.Name)` across `Select(s => s.City)` so `Skip(1).Take(2)` succeeds (`F19`). With `Distinct()`, EF6 discards the sort and throws `NotSupportedException` on `Skip` (`D12`), while succeeding with unordered `TOP (n)` on page 1 (`D12b`). EF Core emits `ORDER BY (SELECT 1) OFFSET ...` (non-deterministic pages).
   - **Consequence:** For entity-level `Distinct()`, `Distinct()` MUST be applied **before** `OrderEvaluator` and `PaginationEvaluator`. For projected `Select`/`SelectMany` + `Distinct()`, entity-level `OrderBy` and `Pagination` cannot order or page the deduplicated `TResult` sequence cleanly:
     - Running `PaginationEvaluator` *before* `Select`/`Distinct` slices pre-distinct rows and yields pages with fewer than `Take` items (`D14`).
     - Running `PaginationEvaluator` *after* `Select().Distinct()` without post-`Distinct` ordering produces non-deterministic paging in EF Core and crashes EF6 on `Skip > 0` (page 2+) — identical to the EF6 `SelectMany` paging hazard already guarded in `src/OpenSpecification.EntityFramework6/PagedQuery.cs`.
     - Deterministic, cross-provider ordering/paging of distinct projections requires either post-projection ordering on `TResult` (placed *after* `Distinct()`) or rejecting/documenting EF6 paging on unordered distinct projections up front (on every page, not only page 2).
2. **EF Core 10.0.14 fails on `Select(new PositionalRecord(...)).Distinct().CountAsync()` and `.OrderBy(...)`.**
   - `Select(new CityCompRec(a, b)).Distinct().ToListAsync()` and `.AnyAsync()` succeed, and `Select(new CityCompRec(a, b)).CountAsync()` (without `Distinct`) succeeds, **but** `.Distinct().CountAsync()` throws `InvalidOperationException: Operation is not valid due to the current state of the object.` and `.Distinct().OrderBy(r => r.City)` fails translation.
   - Member-initialized records/DTOs (`new Dto { A = ..., B = ... }`), anonymous types, scalars, and navigation entities all succeed with `.Distinct().CountAsync()` and `.Distinct().OrderBy(...)` in both EF Core 10 and EF6.
   - Note that `tests/OpenSpecification.EntityFrameworkCore.Tests/Repositories/Repository_CountTests.cs` uses positional records (`record CountryDto(string? Name)`), so `#90` should either document this EF Core limitation or check if a count rewrite is possible (note: `GroupBy(s => new CityCompRec(s.City, s.CompanyId)).CountAsync()` **did** succeed in EF Core 10 (`F18`)!).
3. **In-Memory vs SQL equality divergence for class DTOs.**
   - SQL deduplicates `SELECT DISTINCT` by column values; LINQ-to-Objects uses `EqualityComparer<TResult>.Default` (reference equality for plain classes without `Equals`/`GetHashCode`). Docs and tests must call out that in-memory `Distinct()` on projected DTOs requires value equality (`record`, `struct`, scalar, or `IEquatable<T>`).

---

## 2. `GroupBy(...)` Translation Across EF Core 10, EF6, and LINQ-to-Objects

### 2.1 Empirical Matrix

| Scenario | EF Core 10.0.14 (SQL Server) | EF6 6.5.1 (SQL Server) | LINQ-to-Objects |
|---|---|---|---|
| `GroupBy(key).Select(g => new Dto { Key = g.Key, Count, Sum, Min, Max, Avg })` | **OK** — single `SELECT ... COUNT(*), COALESCE(SUM(...), 0.0), MIN(...), MAX(...), AVG(...) FROM [Stores] GROUP BY [s].[City]` | **OK** — `SELECT ... FROM (SELECT [City] AS [K1], COUNT(1), SUM(...), MIN(...), MAX(...), AVG(...) FROM [Stores] GROUP BY [City]) AS [GroupBy1]` | **OK** |
| `GroupBy(key).Where(g => g.Count() >= 2 && g.Sum(...) > 25m)` (`HAVING`) | **OK** — translates directly to SQL `HAVING COUNT(*) >= 2 AND COALESCE(SUM([s].[Amount]), 0.0) > 25.0` | **OK** — wraps `GROUP BY` subquery with outer `WHERE ([GroupBy1].[A1] >= 2) AND ([GroupBy1].[A2] > 25)` | **OK** |
| `GroupBy(key).Where(HAVING).OrderByDescending(g => g.Sum(...)).ThenBy(g => g.Key).Select(...).Skip(1).Take(2)` | **OK** — single SQL query with `GROUP BY ... HAVING ... ORDER BY ... OFFSET @p FETCH NEXT @p1` | **OK** — EF6 **preserves** `OrderBy` placed on `IGrouping` across `Select(...)`, then applies `ORDER BY row_number() OVER (...) OFFSET 1 FETCH NEXT 2` | **OK** |
| `GroupBy(key).OrderBy(g => g.Key).Skip(1).Take(2).Select(...)` (`Skip/Take` before `Select`) | **OK** — single SQL query with `GROUP BY ... ORDER BY ... OFFSET @p FETCH NEXT @p1` | **OK** — same `row_number() OVER (...) OFFSET/FETCH` SQL | **OK** |
| `GroupBy(key).Select(...).OrderByDescending(r => r.Sum).Skip(1).Take(2)` (post-`Select` ordering) | **OK** — single SQL query | **OK** — same `row_number() OVER (...) OFFSET/FETCH` SQL | **OK** |
| `GroupBy(key).Select(...).Skip(1).Take(2)` (NO `OrderBy`) | **OK (UNORDERED)** — `ORDER BY (SELECT 1) OFFSET @p FETCH NEXT @p1` | **FAIL** — `NotSupportedException: The method 'Skip' is only supported for sorted input in LINQ to Entities.` | **OK** |
| `GroupBy(key).CountAsync()` / `GroupBy(key).Where(HAVING).CountAsync()` | **OK** — counts groups (4 / 3) | **OK** — `SELECT COUNT(1) FROM (SELECT DISTINCT ...)` or `COUNT(1)` over `GROUP BY` subquery (4 / 3) | **OK** |
| `GroupBy(key).Where(HAVING).Select(dto or posRecord).CountAsync()` | **OK** — works even when `Select` projects a positional record (`F9`)! | **OK** (for member-init DTO/anonymous; positional record fails in EF6) | **OK** |
| `GroupBy(key, (k, g) => new Dto { ... })` (`resultSelector` overload) | **OK** — identical SQL to `GroupBy(key).Select(g => ...)` | **OK** — identical SQL to `GroupBy(key).Select(g => ...)` | **OK** |
| `GroupBy(key, (k, g) => ...).Where(r => r.Count >= 2)` and `GroupBy(key).Select(...).Where(r => r.Count >= 2)` | **OK** — translates to SQL `HAVING COUNT(*) >= 2` | **OK** — translates to `WHERE [GroupBy1].[A1] >= 2` over `GroupBy1` subquery | **OK** |
| `GroupBy` + `FirstOrDefaultAsync` / `SingleOrDefaultAsync` / `AnyAsync` | **OK** | **OK** (`TOP (1)` / `TOP (2)` / `EXISTS`) | **OK** |
| Pre-group `OrderBy(s => s.Name).GroupBy(s => s.City).Select(...)` | **DROPS `ORDER BY`** | **DROPS `ORDER BY`** | Grouping preserves encounter order of first key |
| Pre-group `Take(3).GroupBy(s => s.City).Select(...)` | **SLICES ENTITIES BEFORE GROUPING** — `FROM (SELECT TOP(@p) ... ORDER BY ...) GROUP BY` | Same (slices entities before grouping) | Slices entities before grouping |
| Pre-group `TagWith` / `AsNoTracking` / `IgnoreQueryFilters` + `GroupBy` | **OK** — `-- grp-tag` emitted, filter ignored, `GROUP BY` translated | **OK** (`AsNoTracking`) | N/A |
| Pre-group navigation in key (`GroupBy(s => s.Company.Name)`) | **OK** — auto-emits `INNER JOIN [Companies]`, no `Include` needed | **OK** — auto-emits `INNER JOIN [Companies]`, no `Include` needed | Needs populated nav property |
| Conditional aggregate `g.Count(x => x.Amount > 15m)` | **OK** — `COUNT(CASE WHEN [s].[Amount] > 15.0 THEN 1 END)` | **OK** — correlated subquery per distinct key | **OK** |
| `OrderBy(Expression<Func<IGrouping<TKey, T>, object?>>)` with value-type key (`g => g.Count()`) | **OK** — `ORDER BY COUNT(*)` | **FAIL** — `NotSupportedException: Unable to cast the type 'System.Int32' to type 'System.Object'. LINQ to Entities only supports casting EDM primitive or enumeration types.` | **OK** |
| `GroupBy(s => s.City).Select(g => g.Count()).Distinct()` | **OK** — `SELECT DISTINCT COUNT(*) FROM [Stores] GROUP BY [s].[City]` | **OK** — `SELECT DISTINCT [GroupBy1].[A1] FROM (SELECT ... GROUP BY ...)` | **OK** |

### 2.2 Composite Keys Matrix

| Composite Key Shape | C# Expression Tree | EF Core 10.0.14 | EF6 6.5.1 | LINQ-to-Objects |
|---|---|---|---|---|
| Anonymous type `new { s.CompanyId, s.City }` | Compiles | **OK** (`GROUP BY [CompanyId], [City]`) | **OK** (`GROUP BY [City], [CompanyId]`) | **OK** (value equality) |
| Member-init record `new KeyInit { CompanyId = ..., City = ... }` | Compiles | **OK** (`GROUP BY [CompanyId], [City]`) | **OK** (`GROUP BY [City], [CompanyId]`) | **OK** (value equality) |
| Positional record class `new KeyRec(a, b)` | Compiles | **OK** (`GROUP BY [CompanyId], [City]`) | **FAIL** (`Only parameterless constructors and initializers are supported`) | **OK** |
| Positional `readonly record struct` | Compiles | **OK** (`GROUP BY [CompanyId], [City]`) | **FAIL** (`Only parameterless constructors and initializers are supported`) | **OK** |
| `new ValueTuple<int, string?>(a, b)` | Compiles | **OK** (`GROUP BY [CompanyId], [City]`) | **FAIL** (`Only parameterless constructors and initializers are supported`) | **OK** |
| `ValueTuple.Create(a, b)` | Compiles | **FAIL** (`Translation of method 'System.ValueTuple.Create' failed`) | **FAIL** (`LINQ to Entities does not recognize the method ...`) | **OK** |
| C# tuple literal `(s.CompanyId, s.City)` | **COMPILE ERROR `CS8143`**: *An expression tree may not contain a tuple literal* | N/A | N/A | N/A |
| `OrderBy(g => g.Key)` on anonymous composite key | Compiles | **FAIL** (`could not be translated`) | **OK** (sorts by each column) | **FAIL** (`ArgumentException`: anonymous type doesn't implement `IComparable`) |
| `OrderBy(g => g.Key.CompanyId).ThenBy(g => g.Key.City)` | Compiles | **OK** | **OK** | **OK** |

### 2.3 Empty Sequences, `NULL` Keys, and Nullable Aggregates

| Case | EF Core 10.0.14 | EF6 6.5.1 | LINQ-to-Objects |
|---|---|---|---|
| Empty input (`Where(false)`) `GroupBy(key).Select(...)` | `0` groups (no aggregate-on-empty error) | `0` groups | `0` groups |
| Empty input `GroupBy(key).CountAsync()` / `AnyAsync()` | `0` / `false` | `0` / `false` | `0` / `false` |
| Empty input constant key `GroupBy(s => 1).Select(...)` | `0` groups (groups via subquery `SELECT 1 AS [Key] ... GROUP BY [Key]`) | `0` groups | `0` groups |
| `NULL` grouping key (`City == null`) | Grouped together into one `Key == null` group | Grouped together into one `Key == null` group | Grouped together into one `Key == null` group |
| Group where **all** rows have `NULL` in `decimal?`/`int?` — **`g.Sum(x => x.NullableAmount)`** | **`0.00` / `0`** — emits `COALESCE(SUM([s].[NullableAmount]), 0.0)` | **DIVERGENCE: `NULL`** — emits bare `SUM([Extent1].[NullableAmount])` (no `COALESCE`) | **`0m` / `0`** (`Enumerable.Sum` returns `0` for all-null nullable sequences) |
| Group where **all** rows have `NULL` — **`Average` / `Min` / `Max`** | **`NULL`** | **`NULL`** | **`NULL`** |
| `g.Average(x => x.Quantity)` on non-nullable `int` | **`double`** (`AVG(CAST([s].[Quantity] AS float))` -> `1.5`, `3.5`) | **`double`** (`AVG(CAST([Extent1].[Quantity] AS float))` -> `1.5`, `3.5`) | **`double`** (`1.5`, `3.5`) |

### 2.4 Decisive Constraints for `GroupBy` API & Semantics (`#91`, `#92`)

1. **`TKey` should NOT be forced onto the `Specification<...>` class signature if anonymous composite keys are to work cleanly.**
   - Anonymous types (`s => new { s.CompanyId, s.City }`) are the **only** zero-boilerplate composite key supported across EF Core 10, EF6, and LINQ-to-Objects (tuple literals fail `CS8143`, positional records fail EF6). Callers cannot write an anonymous type in a base class generic list (`class MySpec : Specification<Store, ???, GroupDto>`), whereas a method-level `Query.GroupBy(s => new { s.CompanyId, s.City })` infers `TKey` seamlessly on the fluent chain while keeping the specification typed as `Specification<Store, GroupDto>`.
2. **Prefer `GroupBy(keySelector)` -> optional `Where`/`Having` -> optional `OrderBy`/`ThenBy` -> `Select(g => ...)` over `GroupBy(key, (k, g) => ...)` as the internal evaluation model.**
   - Both translate to identical SQL in EF Core 10 and EF6, **but** `GroupBy(key).Where(g => ...).OrderBy(g => ...).Select(g => ...)` uniquely supports `Having` predicates and `OrderBy` expressions referencing aggregates that are *not* projected onto `TResult`.
   - Post-`Select` `Where`/`OrderBy` on `TResult` *also* translates in both EF Core 10 and EF6 (so if `#91` exposes post-select ordering or a `resultSelector` convenience overload, it translates cleanly).
3. **EF6 `OrderBy` boxing (`RemoveConvert`) is mandatory for grouped ordering.**
   - If the builder exposes `OrderBy(Expression<Func<IGrouping<TKey, T>, object?>>)` (mirroring existing `OrderBy<T>`), EF6 throws `NotSupportedException` on any value-type key (`g => g.Count()`, `g => g.Sum(...)`) unless the evaluator strips `ExpressionType.Convert` like `OpenSpecification.EntityFramework6.OrderEvaluator.RemoveConvert` does — OR the builder takes strongly typed `OrderBy<TOrderKey>(Expression<Func<..., TOrderKey>>)` (which avoids boxing altogether in EF6, EF Core, and In-Memory!).
4. **EF6 nullable `Sum` returns `null` when all values in a group are `NULL`, whereas EF Core 10 (`COALESCE`) and LINQ-to-Objects return `0`.**
   - This is an intrinsic EF6 SQL translation difference; tests and docs must account for it (e.g., callers needing consistent `0` across EF6 can use `?? 0`).
5. **Pre-group `OrderBy` and `Skip`/`Take` MUST NOT leak before `GroupBy`.**
   - Pre-group `OrderBy` is dropped by SQL; pre-group `Skip`/`Take` silently truncates raw rows before grouping (`F17`).

---

## 3. Codebase Seams & 10.x Compatibility Constraints

Standing constraint from Map `#88`: `OpenSpecification` targets `net8.0;net9.0;net10.0;netstandard2.0` (no default interface methods). **Zero new members** may be added to `ISpecification<T>`, `ISpecification<T, TResult>`, or `IReadRepositoryBase<T>`.

### 3.1 Inventory of Every Seam That Must Observe `Distinct` / `GroupBy`

| File / Type | Current Behavior | What Must Change / Constraints |
|---|---|---|
| `src/OpenSpecification/Specification.cs` (`Specification<T>`, `Specification<T, TResult>`) | `Specification<T>` has 6 `bool` flags (`IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsSplitQuery`, `AsNoTracking`, `AsTracking`, `AsNoTrackingWithIdentityResolution`). Comment L96-98 notes **2 free alignment bytes on x64**. `tests/OpenSpecification.Tests/SpecificationSizeTests.cs` asserts `ObjectSize.GetObjectInclusiveSize(new Specification<Customer>()) < 105`. | - Adding `bool Distinct` (or `IsDistinct`) to `Specification<T>` costs **0 bytes** on x64 (`SpecificationSizeTests` stays green).<br>- Any `GroupBy` state (a single nullable reference field/descriptor) should live on `Specification<T, TResult>` so `Specification<T>` stays `< 105` bytes.<br>- `CopyState` / `Clone()` / `Clone<TResult>()` (L153-189) must propagate `Distinct` (and decide whether `Clone<TResult>()` preserves `Distinct`).<br>- **10.x contract seam:** `OpenSpecification.csproj` has `InternalsVisibleTo` for `OpenSpecification.EntityFrameworkCore` and `OpenSpecification.EntityFramework6`, AND existing evaluators (`OrderEvaluator`, `IncludeEvaluator`, `WhereEvaluator`) already pattern-match `if (specification is Specification<T> spec)`. Adding **public properties on `Specification<T>` / `Specification<T, TResult>`** (and optionally additive opt-in interfaces like `IDistinctSpecification` / `IGroupBySpecification<T, TResult>` implemented by `Specification<T>` / `Specification<T, TResult>`) lets built-in AND third-party custom `ISpecification` / `IEvaluator` types participate without breaking `ISpecification<T>` / `ISpecification<T, TResult>`. |
| `src/OpenSpecification/Builders/SpecificationBuilder.cs`, `Builder_Select.cs`, `Builder_Flags.cs`, `Builder_Order.cs`, `Builder_Paging.cs` | `ISpecificationBuilder<T>.Specification` exposes concrete `Specification<T>`, and `ISpecificationBuilder<T, TResult>.Specification` exposes concrete `Specification<T, TResult>`. `Builder_Select.cs` defines `Select` and `SelectMany` returning **`void`**. Per-builder `IsChainDiscarded` tracks conditional chains (checked by `formal-builder-chains`). | - **CRITICAL 10.x BINARY-COMPAT CATCH:** Changing `Select` / `SelectMany` from `void` to `ISpecificationBuilder<T, TResult>` is a **binary-breaking change** in .NET (CLR method signatures include the return type, causing `MissingMethodException` for precompiled callers). `Distinct()` must therefore be callable on `ISpecificationBuilder<T>` and `ISpecificationBuilder<T, TResult>` (before `Select`/`SelectMany` or as a separate `Query.Distinct()` statement) without altering `Select`/`SelectMany` return types.<br>- Any conditional builder methods (`Distinct(bool condition)`, grouped `Having(..., bool condition)`, grouped `OrderBy`/`ThenBy(..., bool condition)`) must follow the per-builder `IsChainDiscarded` state machine. |
| `src/OpenSpecification/SpecificationExtensions.cs` (`WithProjectionOf`) | Calls `source.Clone<TResult>()` and copies only `Selector`, `SelectorMany`, and `PostProcessingAction` from `projectionSpec`. | Must explicitly define how `Distinct` (on `source` vs `projectionSpec`) and `GroupBy` (on `projectionSpec`, including conflict with `source`'s `OrderBy`/`Skip`/`Take`/`Include`) are merged or validated. |
| `src/OpenSpecification/Evaluators/IEvaluator.cs` & `IInMemoryEvaluator.cs` | `IEvaluator.GetQuery<T>(IQueryable<T>, ISpecification<T>)` and `IInMemoryEvaluator.Evaluate<T>(IEnumerable<T>, ISpecification<T>)` are strictly `T -> T`. | **Structural boundary:** `IEvaluator` / `IInMemoryEvaluator` can handle entity-level `Distinct()` (for `Specification<T>`), **cannot** change element type (`T -> IGrouping<TKey, T> -> TResult`), and **cannot** run post-`Select`/`SelectMany` `Distinct()`. Those MUST be driven from `GetQuery<T, TResult>` / `Evaluate<T, TResult>` (or a new projection/group evaluator hook invoked there). |
| `src/OpenSpecification/Evaluators/InMemorySpecificationEvaluator.cs` | `Evaluate<T, TResult>` throws `SelectorNotFoundException` if `Selector == null && SelectorMany == null`; calls `Evaluate(source, (ISpecification<T>)spec)` (which runs `Order`, `Pagination`, AND `T`-level `PostProcessingAction`), then applies `Select`/`SelectMany`, then `TResult`-level `PostProcessingAction`. | Must:<br>1. Recognize `GroupBy` before throwing `SelectorNotFoundException` (and reject concurrent `GroupBy` + `Select`/`SelectMany`, e.g., `ConcurrentSelectorsException` or dedicated exception).<br>2. For `GroupBy`: run only pre-group criteria evaluators (`Where`, `SearchMemory`) on `source`, then execute `GroupBy -> Having -> GroupOrder -> GroupSelect -> GroupPagination -> PostProcessingAction` (never run `T`-level `Order`, `Pagination`, or `T`-level `PostProcessingAction` before grouping).<br>3. For `Distinct`: apply entity-level `Distinct` before `Order`/`Pagination`; for projected `Distinct`, determine exact ordering relative to `Pagination` (see §1.2). |
| `src/OpenSpecification.EntityFrameworkCore/Evaluators/SpecificationEvaluator.cs` | `Evaluators` list order: `Where`, `Search`, `IncludeString`, `Include`, `Order`, `Pagination`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsSplitQuery`, `QueryTag`. `GetQuery<T, TResult>` calls `GetQuery(query, (ISpecification<T>)spec)` then `Select`/`SelectMany`. | - Note which evaluators have `IsCriteriaEvaluator == true`: `Where`, `Search`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsSplitQuery`, `QueryTag`. Calling `GetQuery(query, (ISpecification<T>)spec, evaluateCriteriaOnly: true)` cleanly applies all criteria + query-shaping flags (`TagWith`, `IgnoreQueryFilters`, `AsNoTracking`) while excluding `Include`, `Order`, `Pagination`!<br>- For `GroupBy`, `GetQuery<T, TResult>` can start from `GetQuery(query, (ISpecification<T>)spec, evaluateCriteriaOnly: true)` and apply the typed `GroupBy` pipeline on `IQueryable<T>` (via a non-generic/generic dispatch helper on the stored group plan so `TKey` is preserved without reflection-per-call overhead or via cached compiled delegate).<br>- For `Distinct` on `Specification<T>`: must run **before** `OrderEvaluator` and `PaginationEvaluator` (and consider `IsCriteriaEvaluator` for entity counts).<br>- For `Distinct` on `Specification<T, TResult>`: must handle `PaginationEvaluator` ordering so paging applies to distinct rows, not pre-distinct entities. |
| `src/OpenSpecification.EntityFramework6/Evaluators/SpecificationEvaluator.cs` & `OrderEvaluator.cs` | `Evaluators` list order: `Where`, `Search`, `IncludeString`, `Include`, EF6 `OrderEvaluator` (with `RemoveConvert`), `Pagination`, `AsNoTracking`. `GetQuery<T, TResult>` calls `GetQuery(query, (ISpecification<T>)spec)` then `Select`/`SelectMany`. | Same pipeline separation as EF Core, **plus**:<br>- Any `OrderBy(Expression<Func<..., object?>>)` on groups or projected results MUST strip `ExpressionType.Convert` (like EF6 `OrderEvaluator.RemoveConvert`) or use a strongly typed `TOrderKey` generic.<br>- Must respect EF6's strict `OrderBy`-before-`Skip` invariant (no `Distinct` between `OrderBy` and `Skip`). |
| `src/OpenSpecification.EntityFrameworkCore/PagedQuery.cs` & `src/OpenSpecification.EntityFramework6/PagedQuery.cs` | `ToPagedResultAsync<T, TResult>(criteriaQuery, pageQuery, specification, ...)` counts `criteriaQuery` (for `Select`) or `criteriaQuery.SelectMany(...)` (EF Core only; EF6 throws `NotSupportedException` for `SelectMany`), then calls `pageQuery.ApplyPage(pagination).ToListAsync()`. | **Critical seam — currently wrong for both features:**<br>1. `Select` + `Distinct`: must count `criteriaQuery.Select(selector).Distinct()` (not `criteriaQuery`!). Watch EF Core 10's positional-record `Distinct().CountAsync()` bug (`D17`).<br>2. `SelectMany` + `Distinct`: EF Core must count `criteriaQuery.SelectMany(selectorMany).Distinct()`. EF6 already rejects `SelectMany` paging.<br>3. `GroupBy` (+ `Having`): must count the grouped + `Having`-filtered query built on top of `criteriaQuery` (preserving any `RepositoryBase.ApplySpecification` overrides!).<br>4. EF6 `Distinct` paging: if `Distinct` strips entity ordering and no post-distinct ordering exists, EF6 must fail fast on every page (like `SelectMany`), not pass page 1 and crash page 2.<br>5. `PagingExtensions.EnsurePageable`: must also reject specs that have group-level `Skip`/`Take` if stored separately from `Specification.Skip`/`Take`. |
| `RepositoryBase<T>` (EF Core & EF6), `ContextFactoryRepositoryBaseOfT` (EF Core), `DbSetExtensions` (EF Core & EF6) | `ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AsAsyncEnumerable`, `WithSpecification`, `ToListAsync`, `ToEnumerableAsync` all route through `SpecificationEvaluator.GetQuery`. `PagedListAsync`/`ToPagedListAsync` route through `EnsurePageable` + `PagedQuery.ToPagedResultAsync`. `CountAsync(ISpecification<T>)` and `AnyAsync(ISpecification<T>)` call `GetQuery(source, spec, evaluateCriteriaOnly: true)` and count/test `IQueryable<T>`. | - All execution and paging methods work automatically once `SpecificationEvaluator` and `PagedQuery` are updated.<br>- **Watch `CountAsync(ISpecification<T>)` / `AnyAsync(ISpecification<T>)`:** Because `ISpecification<T, TResult> : ISpecification<T>`, callers already pass `Specification<T, TResult>` to `repo.CountAsync(spec)` (see `Repository_CountTests.cs` L84–111). Without v11's `<TResult>` overloads (`#84`), `CountAsync(ISpecification<T>)` will count raw `T` entities unless it inspects `Distinct`/`GroupBy` at runtime — `#90`/`#92` must decide and document this behavior explicitly (currently `CountAsync` on a `SelectMany` spec also counts raw `T` entities). |
| `src/OpenSpecification/Validators/SpecificationValidator.cs` | Runs `WhereValidator` and `SearchValidator` against a single `T entity` for `IsSatisfiedBy(T entity)`. | `Distinct` and `GroupBy`/`Having` are set-level operations, not single-entity predicates. `SpecificationValidator` does not need changes (it continues validating entity-level `Where`/`Search` membership), but docs for `IsSatisfiedBy` should state this explicitly. |

---

## 4. Upstream Context (`ardalis/Specification#138`)

From public issue summaries for `ardalis/Specification#138` (*"Adding GroupBy with post processing selector"*):
- Upstream historically declined built-in `GroupBy` and `Distinct` on `SpecificationBuilder` because `GroupBy` transforms the element type (`T -> IGrouping<TKey, TElement> -> TResult`), which did not fit the `IEvaluator` (`IQueryable<T> -> IQueryable<T>`) pipeline or standard `IRepositoryBase<T>` entity return types.
- Upstream recommended three workarounds: (1) `PostProcessingAction` (loads all filtered rows into memory before grouping/deduplicating — unacceptable for large datasets and ignored by `PagedListAsync`), (2) `DbSet.WithSpecification(spec).Select(...).Distinct()` / `.GroupBy(...)` outside the specification (breaks encapsulation and repository abstraction), or (3) manually iterating `spec.WhereExpressions` in a custom repository method (duplicates evaluator logic and misses `Search`, query filters, etc.).
- `OpenSpecification#88` avoids the raw `IGrouping<TKey, T>` return pitfall (`#85` out of scope) by scoping `GroupBy` to **grouped projections into `TResult`** through existing `ISpecification<T, TResult>` execution and paging paths.

---

## 5. Summary of Hard Constraints for Downstream Tickets

- **For `#90` (`Distinct()` design):**
  - Do **not** change `Select`/`SelectMany` return type from `void` (binary-breaking in 10.x).
  - Entity `Distinct()` must evaluate *before* `OrderEvaluator` and `PaginationEvaluator`.
  - Both EF Core 10 and EF6 silently drop `OrderBy` placed before `Distinct()`, and EF6 throws on `Skip` after `Distinct()` unless ordered *after* `Distinct()`.
  - `PagedQuery` must count `criteriaQuery.Select(selector).Distinct()` / `SelectMany(...).Distinct()` — and account for EF Core 10's `Select(new PositionalRecord(...)).Distinct().CountAsync()` `InvalidOperationException`.
  - `Specification<T>` has 2 free `bool` alignment bytes on x64 (`SpecificationSizeTests` `< 105` bytes).
- **For `#91` (`GroupBy()` prototype) & `#92` (`GroupBy()` design):**
  - Keep `TKey` inferred on the builder method (not a class-level generic parameter on `Specification<T, TResult>`) so anonymous composite keys work across EF Core 10, EF6, and In-Memory (`CS8143` forbids tuple literals in expression trees; EF6 rejects positional records and `ValueTuple`).
  - Use strongly typed `TOrderKey` on group `OrderBy`/`ThenBy` (or strip `ExpressionType.Convert` in EF6) to avoid EF6's `Unable to cast Int32 to Object` failure.
  - Evaluate `GroupBy` on criteria-only `IQueryable<T>` (never letting `T`-level `OrderBy` or `Skip`/`Take` leak before `GroupBy`), and count groups (`GroupBy` + `Having`) on top of `criteriaQuery` in `PagedQuery`.
  - Document the EF6 vs EF Core 10 / In-Memory divergence for `Sum` over all-null nullable columns (`NULL` in EF6 vs `0` in EF Core 10 and LINQ-to-Objects), and EF6's `OrderBy`-before-`Skip` requirement for paged grouped queries.

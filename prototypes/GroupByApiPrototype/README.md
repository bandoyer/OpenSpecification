# Throwaway Prototype: `GroupBy()` Builder API Ergonomics (#91)

> **Primary source for [bandoyer/OpenSpecification#91](https://github.com/bandoyer/OpenSpecification/issues/91)** (parent map [#88](https://github.com/bandoyer/OpenSpecification/issues/88)).
> Lives on throwaway branch `prototype/groupby-builder-api` only; never merged to `main`.

## Question

What should the `GroupBy()` fluent builder API look like in C# so callers get clean type inference, IntelliSense, and compile-time safety when writing grouped projection specifications (`Specification<T, TResult>`), while preserving 10.x compatibility (`ISpecification<T, TResult>` and `IReadRepositoryBase<T>` unchanged)?

## How to Run

```bash
dotnet run --project prototypes/GroupByApiPrototype/GroupByApiPrototype.csproj
```

To verify individual compile-time negative / overload checks:

```bash
for sym in \
  NEG_A_ENTITY_WHERE_AFTER_GROUPBY \
  NEG_A_ENTITY_INCLUDE_AFTER_GROUPBY \
  NEG_A_THENBY_WITHOUT_ORDERBY \
  NEG_A_THENBY_AFTER_HAVING \
  NEG_A_GROUPBY_ON_ENTITY_SPEC \
  NEG_A_FORGOT_SELECT \
  NEG_A_SELECT_AGAIN_AFTER_SELECT \
  NEG_B_FORGOT_SELECT \
  CANDIDATE_B_WITH_DIRECT_OVERLOAD
do
  echo "=== $sym ==="
  dotnet build prototypes/GroupByApiPrototype/GroupByApiPrototype.csproj --no-restore -p:DefineConstants=$sym -v:q
done
```

## Verdict (Confirmed 2026-11-26)

Adopted **Candidate A (Fluent Staged Builder)** ([CandidateA_FluentStaged.cs](CandidateA_FluentStaged.cs)):
- Single 1-arg entry point `ISpecificationBuilder<T, TResult>.GroupBy<T, TKey, TResult>(Expression<Func<T, TKey>> keySelector)` returning an isolated `IGroupedSpecificationBuilder<T, TKey, TResult>` (does not inherit `ISpecificationBuilder<T, TResult>`).
- Strongly typed `TOrderKey` on `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` (EF6-safe, no `object?` boxing), with per-builder `IsOrderChainDiscarded` on `IOrderedGroupedSpecificationBuilder<T, TKey, TResult>`.
- Grouped `.Select(Expression<Func<IGrouping<TKey, T>, TResult>>)` returns an isolated `IGroupedResultSpecificationBuilder<T, TResult>` exposing `.Distinct()`, `.Skip()`, `.Take()`, `.PostProcessingAction()`, and cache methods while keeping entity clauses compile-time unreachable.
- `TKey` and `TOrderKey` are existentially hidden behind a type-erased plan on `Specification<T, TResult>`, keeping `ISpecification<T, TResult>` and `IReadRepositoryBase<T>` unchanged.

---
name: formal-builder-chains
description: Formally checks OpenSpecification's per-builder chain-discarding state machine (IsChainDiscarded) with TLA+. Use when editing SpecificationBuilder.cs, IncludableSpecificationBuilder.cs, Builder_Include.cs, Builder_Order.cs, Builder_Cache.cs, Specification.cs's Query/IsChainDiscarded, or evaluators that consume IncludeExpressions or OrderExpressions.
---

# Formal verification: specification builder chains (`IsChainDiscarded`)

Verifies the immutable per-builder chain-discarding state machine across `Include`/`ThenInclude`, `OrderBy`/`ThenBy`, `EnableCache`/`WithCacheKey`, fluent handoffs, handle branching, and async thread hops.

## Properties verified

1. **`NoOrphanContinuation`:** A `ThenInclude` or `ThenBy`/`ThenByDescending` is never added to a specification after its own chain's root (`Include` or `OrderBy`) or an earlier step was discarded (`condition: false`).
   - *Harm when broken:* Downstream evaluators receive an orphan continuation—`OrderEvaluator.Evaluate` throws `ArgumentNullException` (with two `ThenBy`s) or silently ignores a lone `ThenBy`, EF Core `IncludeEvaluator` fails on invalid `IIncludableQueryable` conversion, and EF6 `IncludeEvaluator` throws `ArgumentException`.
2. **`NoSilentDrop`:** Calling `ThenInclude(..., true)` on an active `Include` chain or `ThenBy(..., true)` on an active `OrderBy` chain always emits that continuation—never silently drops it because of another chain, handle branching, fluent handoff, `EnableCache(..., false)`, or a thread switch.
   - *Harm when broken:* Queries silently omit requested `ThenInclude` navigations or `ThenBy` secondary sorting with no error.

## Coverage record

**Sync commit:** `00bb2e9e9b65e9727493e440a23afef0db39a01b`

Move the sync commit forward only after confirming that `spec/BuilderChains.tla` still describes every file below at the new commit (run `/maintain-formal-verification-skill` after changes that skipped this skill).

| File | Covered members |
|---|---|
| `src/OpenSpecification/Builders/SpecificationBuilder.cs` | Immutable `internal bool IsChainDiscarded` and `SpecificationBuilder<T>` / `SpecificationBuilder<T, TResult>` constructors |
| `src/OpenSpecification/Builders/IncludableSpecificationBuilder.cs` | `IncludableSpecificationBuilder<T, TProperty>` and `IncludableSpecificationBuilder<T, TResult, TProperty>` constructors forwarding `isChainDiscarded` |
| `src/OpenSpecification/Builders/Builder_Include.cs` | `Include(..., bool condition)` and `ThenInclude(..., bool condition)` overloads |
| `src/OpenSpecification/Builders/Builder_Order.cs` | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `AddOrderStart`, and `AddOrderContinuation` |
| `src/OpenSpecification/Builders/Builder_Cache.cs` | `EnableCache(..., bool condition)` and `WithCacheKey(..., bool condition)` (no-op on `IsChainDiscarded`) |
| `src/OpenSpecification/Specification.cs` | `Query` builder factory and unused `[Obsolete] [ThreadStatic] IsChainDiscarded` compatibility field |
| `src/OpenSpecification/Evaluators/OrderEvaluator.cs` | Consumer of `OrderExpressions` sequence invariant |
| `src/OpenSpecification.EntityFrameworkCore/Evaluators/IncludeEvaluator.cs` | Consumer of `IncludeExpressions` sequence invariant |
| `src/OpenSpecification.EntityFramework6/Evaluators/IncludeEvaluator.cs` | Consumer of `IncludeExpressions` sequence invariant |

Before running, check whether covered code moved or changed since the sync commit:

```bash
git diff 00bb2e9e9b65e9727493e440a23afef0db39a01b..HEAD -- \
  src/OpenSpecification/Specification.cs \
  src/OpenSpecification/Builders/SpecificationBuilder.cs \
  src/OpenSpecification/Builders/IncludableSpecificationBuilder.cs \
  src/OpenSpecification/Builders/Builder_Include.cs \
  src/OpenSpecification/Builders/Builder_Order.cs \
  src/OpenSpecification/Builders/Builder_Cache.cs
```

## Run the check

Run through skillflow's `bin/sandbox` inside a skillflow run.

- **Full model (saved handles, fluent handoffs, handle branching, `EnableCache`, and `await` thread hops):**
  ```bash
  .agents/skills/formal-builder-chains/scripts/check full
  ```
- **Linear-only subset (single inline fluent chain on one thread):**
  ```bash
  .agents/skills/formal-builder-chains/scripts/check linear
  ```

**Always read every TLA+ result with the `/statechart` skill**, pointing it at `spec/BuilderChains.tla`, `spec/BuilderChains.cfg` (or `spec/LinearChains.cfg`), and `spec/BuilderChains.view.mjs`. `/statechart` prints each broken invariant's shortest path in plain words and draws the statechart when the model fits its state budget.

## Current status at sync commit `00bb2e9`

- **`full` (`BuilderChains.cfg`): PASSES** (49 distinct states, 959 transitions; `TypeOK`, `NoOrphanContinuation`, `NoSilentDrop`, and `NoDeadlock` all hold).
- **`linear` (`LinearChains.cfg`): PASSES** (5 distinct states, 28 transitions; `TypeOK`, `NoOrphanContinuation`, `NoSilentDrop`, and `NoDeadlock` all hold).

Keep all four invariants intact when updating the model; fix any violation in the C# code or by correcting a modeling mismatch against the C# code.

## Judging failures and updating the model

1. **Real bug vs. modeling error:** Replay every step of a counterexample in real C# against `Specification<T>` (one C# call per TLA+ action). If the C# code exhibits the failure, it is a real bug; if a TLA+ step cannot happen in C#, fix `spec/BuilderChains.tla` so every action matches the real C# code one-to-one.
2. **Intended design change:** Update `spec/BuilderChains.tla` directly from the new C# code, rerun both `full` and `linear` (and read results with `/statechart`), confirm a scratch break makes TLC fail, revert the scratch break, and update `spec/BuilderChains.view.mjs`, the Coverage record, and the sync commit here.

## Scope and limits

- **Verified:** All reachable sequences of `Include`/`ThenInclude`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, fluent handoffs across chains (`Include` → `OrderBy` and `OrderBy` → `Include`), branching from saved handles, `EnableCache`/`WithCacheKey`, and async thread hops (exact finite-state model: 49 distinct states, 959 transitions, no step-depth bound needed). Independent handles on one specification also represent separate `Specification<T>` instances because no discard state is stored statically.
- **Not covered:** `Include(string)` overloads (they do not touch `IsChainDiscarded`), `DuplicateOrderChainException` for two `OrderBy` calls, `Specification<T>.Clone()`, expression-tree validation, and EF Core/EF6 SQL translation (covered by `verify-specification`).

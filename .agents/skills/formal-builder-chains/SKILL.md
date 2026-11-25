---
name: formal-builder-chains
description: Formally checks OpenSpecification's builder chain-discarding state machine (Specification<T>.IsChainDiscarded) with TLA+. Use when editing Specification.cs's IsChainDiscarded, Builder_Include.cs, Builder_Order.cs, Builder_Cache.cs, IncludableSpecificationBuilder.cs, SpecificationBuilder.cs, or evaluators that consume IncludeExpressions or OrderExpressions.
---

# Formal verification: specification builder chains (`IsChainDiscarded`)

Verifies the conditional chain-discarding state machine shared by `Include`/`ThenInclude`, `OrderBy`/`ThenBy`, and `EnableCache`/`WithCacheKey`.

## Properties verified

1. **`NoOrphanContinuation`:** A `ThenInclude` or `ThenBy`/`ThenByDescending` is never added to a specification after its own chain's root (`Include` or `OrderBy`) or an earlier step was discarded (`condition: false`).
   - *Harm when broken:* Downstream evaluators receive an orphan continuation—`OrderEvaluator.Evaluate` throws `ArgumentNullException` (with two `ThenBy`s) or silently ignores a lone `ThenBy`, EF Core `IncludeEvaluator` fails on invalid `IIncludableQueryable` conversion, and EF6 `IncludeEvaluator` either includes the wrong navigation path (single-item fast path) or throws `ArgumentException` (multi-item loop).
2. **`NoSilentDrop`:** Calling `ThenInclude(..., true)` on an active `Include` chain or `ThenBy(..., true)` on an active `OrderBy` chain always emits that continuation—never silently drops it because of another chain, `EnableCache(..., false)`, or a thread switch.
   - *Harm when broken:* Queries silently omit requested `ThenInclude` navigations or `ThenBy` secondary sorting with no error.

## Coverage record

**Sync commit:** `52d8fd7df7b76fb5b51c0e1d018f51788b1e55cc`

Move the sync commit forward only after confirming that `spec/BuilderChains.tla` still describes every file below at the new commit (run `/maintain-formal-verification-skill` after changes that skipped this skill).

| File | Covered members |
|---|---|
| `src/OpenSpecification/Specification.cs` | `[ThreadStatic] public static bool IsChainDiscarded` (lines 38–40) |
| `src/OpenSpecification/Builders/Builder_Include.cs` | `Include(..., bool condition)` and `ThenInclude(..., bool condition)` overloads |
| `src/OpenSpecification/Builders/Builder_Order.cs` | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` conditional overloads |
| `src/OpenSpecification/Builders/Builder_Cache.cs` | `EnableCache(..., bool condition)` and `WithCacheKey(..., bool condition)` |
| `src/OpenSpecification/Evaluators/OrderEvaluator.cs` | Consumer of `OrderExpressions` sequence invariant |
| `src/OpenSpecification.EntityFrameworkCore/Evaluators/IncludeEvaluator.cs` | Consumer of `IncludeExpressions` sequence invariant |
| `src/OpenSpecification.EntityFramework6/Evaluators/IncludeEvaluator.cs` | Consumer of `IncludeExpressions` sequence invariant |

Before running, check whether covered code moved or changed since the sync commit:

```bash
git diff 52d8fd7df7b76fb5b51c0e1d018f51788b1e55cc..HEAD -- \
  src/OpenSpecification/Specification.cs \
  src/OpenSpecification/Builders/Builder_Include.cs \
  src/OpenSpecification/Builders/Builder_Order.cs \
  src/OpenSpecification/Builders/Builder_Cache.cs
```

## Run the check

Run through skillflow's `bin/sandbox` inside a skillflow run.

- **Full model (general builder usage — saved handles, `EnableCache`, and `await` thread hops):**
  ```bash
  .agents/skills/formal-builder-chains/scripts/check full
  ```
- **Linear-only subset (single inline fluent chain on one thread):**
  ```bash
  .agents/skills/formal-builder-chains/scripts/check linear
  ```

**Always read every TLA+ result with the `/statechart` skill**, pointing it at `spec/BuilderChains.tla`, `spec/BuilderChains.cfg` (or `spec/LinearChains.cfg`), and `spec/BuilderChains.view.mjs`. `/statechart` prints each broken invariant's shortest path in plain words and draws the statechart when the model fits its state budget.

## Current status at sync commit `52d8fd7`

- **`linear` (`LinearChains.cfg`): PASSES** (all 5 distinct states, 28 steps satisfy `NoOrphanContinuation`, `NoSilentDrop`, `TypeOK`, and `NoDeadlock`). Any change that breaks single-chain conditional discarding turns `linear` red.
- **`full` (`BuilderChains.cfg`): FAILS on two confirmed bugs in the current code** (132 states, 1308 transitions):
  1. **`NoOrphanContinuation` breaks in 3 steps:** `Include(false)` sets `IsChainDiscarded = true` → `OrderBy(true)` (or `EnableCache(true)` or an `await` thread hop) resets `IsChainDiscarded = false` → `ThenInclude(true)` emits an orphan `ThenInclude`.
  2. **`NoSilentDrop` breaks in 3 steps:** `Include(true)` sets `IsChainDiscarded = false` → `OrderBy(false)` (or `EnableCache(false)` or a thread hop to a thread with `IsChainDiscarded = true`) sets `IsChainDiscarded = true` → `ThenInclude(true)` silently drops the continuation.

Both bugs were replayed directly against `OpenSpecification` and `OpenSpecification.EntityFramework6` at `52d8fd7`. Do not weaken the invariants to make `full` pass.

## Judging failures and updating the model

1. **Real bug vs. modeling error:** Replay every step of a counterexample in real C# against `Specification<T>` (one C# call per TLA+ action). If the C# code exhibits the failure, it is a real bug; if a TLA+ step cannot happen in C#, fix `spec/BuilderChains.tla` so every action matches the real C# code one-to-one.
2. **Intended design change:** If you refactor chain-discarding state (for example, moving `IsChainDiscarded` from `[ThreadStatic]` onto each builder instance so each handle owns its own flag), update `spec/BuilderChains.tla` directly from the new C# code, rerun both `full` and `linear` (and read results with `/statechart`), confirm a scratch break in the new code makes TLC fail, revert the scratch break, and update the Coverage record and sync commit here.

## Scope and limits

- **Verified:** All reachable sequences of `Include`/`ThenInclude`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, `EnableCache`/`WithCacheKey`, and thread switches across two threads on one `Specification<T>` (exact finite-state model: 132 states, no step-depth bound needed).
- **Not covered:** `Include(string)` overloads (they do not touch `IsChainDiscarded`), `DuplicateOrderChainException` for two `OrderBy` calls, `Specification<T>.Clone()`, expression-tree validation, and EF Core/EF6 SQL translation (covered by `verify-specification`).

---- MODULE BuilderChains ----
\* Formal model of OpenSpecification's builder chain-discarding state machine:
\*   - src/OpenSpecification/Builders/SpecificationBuilder.cs (immutable IsChainDiscarded)
\*   - src/OpenSpecification/Builders/IncludableSpecificationBuilder.cs (immutable IsChainDiscarded)
\*   - src/OpenSpecification/Builders/Builder_Include.cs (Include / ThenInclude)
\*   - src/OpenSpecification/Builders/Builder_Order.cs (OrderBy / OrderByDescending / ThenBy / ThenByDescending)
\*   - src/OpenSpecification/Builders/Builder_Cache.cs (EnableCache / WithCacheKey)
\*   - src/OpenSpecification/Specification.cs (Query factory; unused [Obsolete] static IsChainDiscarded)
\*
\* Sync commit: 00bb2e9e9b65e9727493e440a23afef0db39a01b
\*
\* Each caller handle (primary Include handle, branched Include handle, primary
\* OrderBy handle, branched OrderBy handle) has a ground-truth chain status
\* ("none", "active", "discarded") and an immutable per-builder IsChainDiscarded
\* boolean stored on that builder instance (not in [ThreadStatic]).
\*
\* Two independent handles on one specification behave identically to handles on
\* separate Specification<T> instances, because no discard state is shared statically.

EXTENDS Naturals

VARIABLES
    incState,       \* Ground truth of primary Include handle: "none" | "active" | "discarded"
    incFlag,        \* Primary IncludableSpecificationBuilder.IsChainDiscarded
    incBranchState, \* Ground truth of branched Include handle (var b = inc.ThenInclude(...))
    incBranchFlag,  \* Branched IncludableSpecificationBuilder.IsChainDiscarded
    ordState,       \* Ground truth of primary OrderBy handle: "none" | "active" | "discarded"
    ordFlag,        \* Primary SpecificationBuilder.IsChainDiscarded
    ordBranchState, \* Ground truth of branched OrderBy handle (var b = ord.ThenBy(...))
    ordBranchFlag,  \* Branched SpecificationBuilder.IsChainDiscarded
    orphanEmitted,  \* TRUE if ThenInclude/ThenBy was emitted when its own chain was discarded
    activeDropped   \* TRUE if ThenInclude(true)/ThenBy(true) was skipped while its own chain was active

vars == <<incState, incFlag, incBranchState, incBranchFlag,
          ordState, ordFlag, ordBranchState, ordBranchFlag,
          orphanEmitted, activeDropped>>

Init ==
    /\ incState = "none"
    /\ incFlag = FALSE
    /\ incBranchState = "none"
    /\ incBranchFlag = FALSE
    /\ ordState = "none"
    /\ ordFlag = FALSE
    /\ ordBranchState = "none"
    /\ ordBranchFlag = FALSE
    /\ orphanEmitted = FALSE
    /\ activeDropped = FALSE

\* Query.Include(expr, condition: true) — Builder_Include.cs:102-109, 139-146
\* Allocates new IncludableSpecificationBuilder(spec, isChainDiscarded: false).
IncludeTrue ==
    /\ incState' = "active"
    /\ incFlag' = FALSE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Query.Include(expr, condition: false) — Builder_Include.cs:102-109, 139-146
\* Allocates new IncludableSpecificationBuilder(spec, isChainDiscarded: true).
IncludeFalse ==
    /\ incState' = "discarded"
    /\ incFlag' = TRUE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Fluent handoff: ord.Include(expr, condition: true) — Builder_Include.cs:102-109, 139-146
\* Always allocates a fresh IncludableSpecificationBuilder with !condition (ignores ordFlag).
IncludeTrueFromOrder ==
    /\ ordState \in {"active", "discarded"}
    /\ incState' = "active"
    /\ incFlag' = FALSE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Fluent handoff: ord.Include(expr, condition: false) — Builder_Include.cs:102-109, 139-146
IncludeFalseFromOrder ==
    /\ ordState \in {"active", "discarded"}
    /\ incState' = "discarded"
    /\ incFlag' = TRUE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* inc = inc.ThenInclude(expr, condition: true) — Builder_Include.cs:182-190, 224-232, 268-276, 310-318
ThenIncludeTrue ==
    /\ incState \in {"active", "discarded"}
    /\ LET isDiscarded == incFlag
           emitted == ~isDiscarded IN
       /\ incFlag' = isDiscarded
       /\ orphanEmitted' = (orphanEmitted \/ (incState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (incState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incBranchState, incBranchFlag,
                   ordState, ordFlag, ordBranchState, ordBranchFlag>>

\* inc = inc.ThenInclude(expr, condition: false) — Builder_Include.cs:182-190, 224-232, 268-276, 310-318
ThenIncludeFalse ==
    /\ incState \in {"active", "discarded"}
    /\ incState' = "discarded"
    /\ incFlag' = TRUE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Branch from saved handle: incBranch = inc.ThenInclude(expr, condition: true)
\* Allocates a new IncludableSpecificationBuilder; inc's immutable IsChainDiscarded is unchanged.
BranchThenIncludeTrue ==
    /\ incState \in {"active", "discarded"}
    /\ LET isDiscarded == incFlag
           emitted == ~isDiscarded IN
       /\ incBranchState' = incState
       /\ incBranchFlag' = isDiscarded
       /\ orphanEmitted' = (orphanEmitted \/ (incState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (incState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incFlag, ordState, ordFlag, ordBranchState, ordBranchFlag>>

\* Branch from saved handle: incBranch = inc.ThenInclude(expr, condition: false)
\* Allocates a new discarded IncludableSpecificationBuilder; inc remains unchanged.
BranchThenIncludeFalse ==
    /\ incState \in {"active", "discarded"}
    /\ incBranchState' = "discarded"
    /\ incBranchFlag' = TRUE
    /\ UNCHANGED <<incState, incFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* incBranch = incBranch.ThenInclude(expr, condition: true)
ContinueIncBranchTrue ==
    /\ incBranchState \in {"active", "discarded"}
    /\ LET isDiscarded == incBranchFlag
           emitted == ~isDiscarded IN
       /\ incBranchFlag' = isDiscarded
       /\ orphanEmitted' = (orphanEmitted \/ (incBranchState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (incBranchState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incFlag, incBranchState,
                   ordState, ordFlag, ordBranchState, ordBranchFlag>>

\* incBranch = incBranch.ThenInclude(expr, condition: false)
ContinueIncBranchFalse ==
    /\ incBranchState \in {"active", "discarded"}
    /\ incBranchState' = "discarded"
    /\ incBranchFlag' = TRUE
    /\ UNCHANGED <<incState, incFlag, ordState, ordFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Query.OrderBy / OrderByDescending(expr, condition: true) — Builder_Order.cs:227-234, 245-252
OrderByTrue ==
    /\ ordState' = "active"
    /\ ordFlag' = FALSE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Query.OrderBy / OrderByDescending(expr, condition: false) — Builder_Order.cs:236, 254
OrderByFalse ==
    /\ ordState' = "discarded"
    /\ ordFlag' = TRUE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Fluent handoff: inc.OrderBy(expr, condition: true) — Builder_Order.cs:227-234, 245-252
\* Reuses existing builder only when IsChainDiscarded == false; otherwise allocates a fresh active builder.
OrderByTrueFromInclude ==
    /\ incState \in {"active", "discarded"}
    /\ ordState' = "active"
    /\ ordFlag' = IF ~incFlag THEN incFlag ELSE FALSE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Fluent handoff: inc.OrderBy(expr, condition: false) — Builder_Order.cs:236, 254
\* Allocates a fresh discarded SpecificationBuilder; inc's flag is unchanged.
OrderByFalseFromInclude ==
    /\ incState \in {"active", "discarded"}
    /\ ordState' = "discarded"
    /\ ordFlag' = TRUE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* ord = ord.ThenBy / ThenByDescending(expr, condition: true) — Builder_Order.cs:263-271, 280-288
ThenByTrue ==
    /\ ordState \in {"active", "discarded"}
    /\ LET isDiscarded == ordFlag
           emitted == ~isDiscarded IN
       /\ ordFlag' = ordFlag
       /\ orphanEmitted' = (orphanEmitted \/ (ordState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (ordState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordState, ordBranchState, ordBranchFlag>>

\* ord = ord.ThenBy / ThenByDescending(expr, condition: false) — Builder_Order.cs:263-271, 280-288
ThenByFalse ==
    /\ ordState \in {"active", "discarded"}
    /\ ordState' = "discarded"
    /\ ordFlag' = IF ordFlag THEN ordFlag ELSE TRUE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordBranchState, ordBranchFlag, orphanEmitted, activeDropped>>

\* Branch from saved handle: ordBranch = ord.ThenBy(expr, condition: true)
\* Returns ord unchanged (immutable flag); ord remains untouched.
BranchThenByTrue ==
    /\ ordState \in {"active", "discarded"}
    /\ LET isDiscarded == ordFlag
           emitted == ~isDiscarded IN
       /\ ordBranchState' = ordState
       /\ ordBranchFlag' = ordFlag
       /\ orphanEmitted' = (orphanEmitted \/ (ordState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (ordState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordState, ordFlag>>

\* Branch from saved handle: ordBranch = ord.ThenBy(expr, condition: false)
\* Allocates a new discarded SpecificationBuilder when ord is active; ord remains untouched.
BranchThenByFalse ==
    /\ ordState \in {"active", "discarded"}
    /\ ordBranchState' = "discarded"
    /\ ordBranchFlag' = IF ordFlag THEN ordFlag ELSE TRUE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordState, ordFlag, orphanEmitted, activeDropped>>

\* ordBranch = ordBranch.ThenBy(expr, condition: true)
ContinueOrdBranchTrue ==
    /\ ordBranchState \in {"active", "discarded"}
    /\ LET isDiscarded == ordBranchFlag
           emitted == ~isDiscarded IN
       /\ ordBranchFlag' = ordBranchFlag
       /\ orphanEmitted' = (orphanEmitted \/ (ordBranchState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (ordBranchState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordState, ordFlag, ordBranchState>>

\* ordBranch = ordBranch.ThenBy(expr, condition: false)
ContinueOrdBranchFalse ==
    /\ ordBranchState \in {"active", "discarded"}
    /\ ordBranchState' = "discarded"
    /\ ordBranchFlag' = IF ordBranchFlag THEN ordBranchFlag ELSE TRUE
    /\ UNCHANGED <<incState, incFlag, incBranchState, incBranchFlag,
                   ordState, ordFlag, orphanEmitted, activeDropped>>

\* EnableCache / WithCacheKey(..., condition: true | false) — Builder_Cache.cs:76-87, 152-163
\* Returns (SpecificationBuilder<T>)builder without reading or writing any IsChainDiscarded flag.
\* Chaining OrderBy/Include onto its return value therefore matches OrderBy*FromInclude / Include*FromOrder.
EnableCacheTrue ==
    UNCHANGED vars

EnableCacheFalse ==
    UNCHANGED vars

\* Async continuation resumes on another thread.
\* Per-builder IsChainDiscarded lives on the heap builder instance, not [ThreadStatic],
\* so thread hops leave every handle's flag unchanged.
SwitchThread ==
    UNCHANGED vars

\* Full model: any legal sequence of public builder calls (saved handles, fluent handoffs,
\* branching from saved handles, EnableCache/WithCacheKey, and async thread hops).
Next ==
    \/ IncludeTrue
    \/ IncludeFalse
    \/ IncludeTrueFromOrder
    \/ IncludeFalseFromOrder
    \/ ThenIncludeTrue
    \/ ThenIncludeFalse
    \/ BranchThenIncludeTrue
    \/ BranchThenIncludeFalse
    \/ ContinueIncBranchTrue
    \/ ContinueIncBranchFalse
    \/ OrderByTrue
    \/ OrderByFalse
    \/ OrderByTrueFromInclude
    \/ OrderByFalseFromInclude
    \/ ThenByTrue
    \/ ThenByFalse
    \/ BranchThenByTrue
    \/ BranchThenByFalse
    \/ ContinueOrdBranchTrue
    \/ ContinueOrdBranchFalse
    \/ EnableCacheTrue
    \/ EnableCacheFalse
    \/ SwitchThread

Spec == Init /\ [][Next]_vars

\* Linear-only subset: strictly one fluent chain at a time on one thread,
\* starting a new chain retires the old handle (models inline Query.Include(...).ThenInclude(...)).
LinearIncludeTrue ==
    /\ incState' = "active"
    /\ incFlag' = FALSE
    /\ ordState' = "none"
    /\ ordFlag' = FALSE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordBranchState, ordBranchFlag,
                   orphanEmitted, activeDropped>>

LinearIncludeFalse ==
    /\ incState' = "discarded"
    /\ incFlag' = TRUE
    /\ ordState' = "none"
    /\ ordFlag' = FALSE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordBranchState, ordBranchFlag,
                   orphanEmitted, activeDropped>>

LinearOrderByTrue ==
    /\ incState' = "none"
    /\ incFlag' = FALSE
    /\ ordState' = "active"
    /\ ordFlag' = FALSE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordBranchState, ordBranchFlag,
                   orphanEmitted, activeDropped>>

LinearOrderByFalse ==
    /\ incState' = "none"
    /\ incFlag' = FALSE
    /\ ordState' = "discarded"
    /\ ordFlag' = TRUE
    /\ UNCHANGED <<incBranchState, incBranchFlag, ordBranchState, ordBranchFlag,
                   orphanEmitted, activeDropped>>

NextLinear ==
    \/ LinearIncludeTrue
    \/ LinearIncludeFalse
    \/ ThenIncludeTrue
    \/ ThenIncludeFalse
    \/ LinearOrderByTrue
    \/ LinearOrderByFalse
    \/ ThenByTrue
    \/ ThenByFalse

SpecLinear == Init /\ [][NextLinear]_vars

\* Promise 1: Never emit a ThenInclude or ThenBy when its own chain was discarded.
NoOrphanContinuation == ~orphanEmitted

\* Promise 2: Never silently drop a ThenInclude(true) or ThenBy(true) while its own chain is active.
NoSilentDrop == ~activeDropped

TypeOK ==
    /\ incState \in {"none", "active", "discarded"}
    /\ incFlag \in BOOLEAN
    /\ incBranchState \in {"none", "active", "discarded"}
    /\ incBranchFlag \in BOOLEAN
    /\ ordState \in {"none", "active", "discarded"}
    /\ ordFlag \in BOOLEAN
    /\ ordBranchState \in {"none", "active", "discarded"}
    /\ ordBranchFlag \in BOOLEAN
    /\ orphanEmitted \in BOOLEAN
    /\ activeDropped \in BOOLEAN

====

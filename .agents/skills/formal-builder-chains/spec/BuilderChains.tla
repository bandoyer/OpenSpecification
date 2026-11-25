---- MODULE BuilderChains ----
\* Formal model of OpenSpecification's builder chain-discarding state machine:
\*   - src/OpenSpecification/Specification.cs:38-40 (IsChainDiscarded, [ThreadStatic])
\*   - src/OpenSpecification/Builders/Builder_Include.cs (Include / ThenInclude)
\*   - src/OpenSpecification/Builders/Builder_Order.cs (OrderBy / OrderByDescending / ThenBy / ThenByDescending)
\*   - src/OpenSpecification/Builders/Builder_Cache.cs (EnableCache / WithCacheKey)
\*
\* Sync commit: 52d8fd7df7b76fb5b51c0e1d018f51788b1e55cc
\*
\* Each caller handle (Include/ThenInclude and OrderBy/ThenBy) has a ground-truth
\* status ("none", "active", "discarded"). The C# implementation stores only one
\* [ThreadStatic] boolean (Specification<T>.IsChainDiscarded) shared across all
\* chains and specifications on the current thread.

EXTENDS Naturals

VARIABLES
    incState,       \* Ground truth of the Include handle: "none" | "active" | "discarded"
    ordState,       \* Ground truth of the OrderBy handle: "none" | "active" | "discarded"
    flag,           \* Current thread's Specification<T>.IsChainDiscarded
    otherFlag,      \* Another thread's Specification<T>.IsChainDiscarded (for async thread hop)
    orphanEmitted,  \* TRUE if ThenInclude/ThenBy was emitted when its own chain was discarded
    activeDropped   \* TRUE if ThenInclude(true)/ThenBy(true) was skipped while its own chain was active

vars == <<incState, ordState, flag, otherFlag, orphanEmitted, activeDropped>>

Init ==
    /\ incState = "none"
    /\ ordState = "none"
    /\ flag = FALSE
    /\ otherFlag = FALSE
    /\ orphanEmitted = FALSE
    /\ activeDropped = FALSE

\* Include(expr, condition: true) — Builder_Include.cs:102-109, 140-147
IncludeTrue ==
    /\ incState' = "active"
    /\ flag' = FALSE
    /\ UNCHANGED <<ordState, otherFlag, orphanEmitted, activeDropped>>

\* Include(expr, condition: false) — Builder_Include.cs:102-109, 140-147
IncludeFalse ==
    /\ incState' = "discarded"
    /\ flag' = TRUE
    /\ UNCHANGED <<ordState, otherFlag, orphanEmitted, activeDropped>>

\* ThenInclude(expr, condition: true) — Builder_Include.cs:184-195, 229-240, 276-287, 321-332
ThenIncludeTrue ==
    /\ incState \in {"active", "discarded"}
    /\ LET emitted == ~flag IN
       /\ flag' = IF emitted THEN flag ELSE TRUE
       /\ orphanEmitted' = (orphanEmitted \/ (incState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (incState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, ordState, otherFlag>>

\* ThenInclude(expr, condition: false) — Builder_Include.cs:184-195, 229-240, 276-287, 321-332
ThenIncludeFalse ==
    /\ incState \in {"active", "discarded"}
    /\ incState' = "discarded"
    /\ flag' = TRUE
    /\ UNCHANGED <<ordState, otherFlag, orphanEmitted, activeDropped>>

\* OrderBy / OrderByDescending(expr, condition: true) — Builder_Order.cs:64-71, 133-140
OrderByTrue ==
    /\ ordState' = "active"
    /\ flag' = FALSE
    /\ UNCHANGED <<incState, otherFlag, orphanEmitted, activeDropped>>

\* OrderBy / OrderByDescending(expr, condition: false) — Builder_Order.cs:64-71, 133-140
OrderByFalse ==
    /\ ordState' = "discarded"
    /\ flag' = TRUE
    /\ UNCHANGED <<incState, otherFlag, orphanEmitted, activeDropped>>

\* ThenBy / ThenByDescending(expr, condition: true) — Builder_Order.cs:202-212, 274-284
ThenByTrue ==
    /\ ordState \in {"active", "discarded"}
    /\ LET emitted == ~flag IN
       /\ flag' = IF emitted THEN flag ELSE TRUE
       /\ orphanEmitted' = (orphanEmitted \/ (ordState = "discarded" /\ emitted))
       /\ activeDropped' = (activeDropped \/ (ordState = "active" /\ ~emitted))
    /\ UNCHANGED <<incState, ordState, otherFlag>>

\* ThenBy / ThenByDescending(expr, condition: false) — Builder_Order.cs:202-212, 274-284
ThenByFalse ==
    /\ ordState \in {"active", "discarded"}
    /\ ordState' = "discarded"
    /\ flag' = TRUE
    /\ UNCHANGED <<incState, otherFlag, orphanEmitted, activeDropped>>

\* EnableCache / WithCacheKey(..., condition: true) — Builder_Cache.cs:76-87, 153-164
EnableCacheTrue ==
    /\ flag' = FALSE
    /\ UNCHANGED <<incState, ordState, otherFlag, orphanEmitted, activeDropped>>

\* EnableCache / WithCacheKey(..., condition: false) — Builder_Cache.cs:76-87, 153-164
EnableCacheFalse ==
    /\ flag' = TRUE
    /\ UNCHANGED <<incState, ordState, otherFlag, orphanEmitted, activeDropped>>

\* Async continuation resumes on another thread with its own [ThreadStatic] IsChainDiscarded
SwitchThread ==
    /\ flag' = otherFlag
    /\ otherFlag' = flag
    /\ UNCHANGED <<incState, ordState, orphanEmitted, activeDropped>>

\* Full model: any legal sequence of public builder calls (including saved handles,
\* EnableCache/WithCacheKey, and async thread hops).
Next ==
    \/ IncludeTrue
    \/ IncludeFalse
    \/ ThenIncludeTrue
    \/ ThenIncludeFalse
    \/ OrderByTrue
    \/ OrderByFalse
    \/ ThenByTrue
    \/ ThenByFalse
    \/ EnableCacheTrue
    \/ EnableCacheFalse
    \/ SwitchThread

Spec == Init /\ [][Next]_vars

\* Linear-only subset: strictly one fluent chain at a time on one thread,
\* starting a new chain retires the old handle (models inline Query.Include(...).ThenInclude(...)).
LinearIncludeTrue ==
    /\ incState' = "active"
    /\ ordState' = "none"
    /\ flag' = FALSE
    /\ UNCHANGED <<otherFlag, orphanEmitted, activeDropped>>

LinearIncludeFalse ==
    /\ incState' = "discarded"
    /\ ordState' = "none"
    /\ flag' = TRUE
    /\ UNCHANGED <<otherFlag, orphanEmitted, activeDropped>>

LinearOrderByTrue ==
    /\ incState' = "none"
    /\ ordState' = "active"
    /\ flag' = FALSE
    /\ UNCHANGED <<otherFlag, orphanEmitted, activeDropped>>

LinearOrderByFalse ==
    /\ incState' = "none"
    /\ ordState' = "discarded"
    /\ flag' = TRUE
    /\ UNCHANGED <<otherFlag, orphanEmitted, activeDropped>>

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
    /\ ordState \in {"none", "active", "discarded"}
    /\ flag \in BOOLEAN
    /\ otherFlag \in BOOLEAN
    /\ orphanEmitted \in BOOLEAN
    /\ activeDropped \in BOOLEAN

====

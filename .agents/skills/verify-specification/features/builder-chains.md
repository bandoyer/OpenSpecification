# Builder chain isolation

Since #78, each `Include`/`ThenInclude` and `OrderBy`/`ThenBy` builder handle carries its own immutable discard flag instead of sharing `[ThreadStatic] Specification<T>.IsChainDiscarded`. Interleaving builder handles, calling `EnableCache`/`WithCacheKey`, or continuing a saved handle on another thread neither emits orphan `ThenInclude`/`ThenBy` entries nor drops active continuations.

## Sub-features

- `orphan-include-via-orderby`: `Include(..., false)` followed by `OrderBy(..., true)` then `ThenInclude(...)` emits no include expressions and lists rows without throwing.
- `orphan-orderby-via-cache`: `OrderBy(..., false)` followed by `EnableCache(..., true)` then `ThenBy(...).ThenBy(...)` emits no order expressions and runs both SQL listing and in-memory `Evaluate` without throwing `ArgumentNullException`.
- `drop-include-via-orderby`: `Include(..., true)` followed by `OrderBy(..., false)` then `ThenInclude(...)` keeps both `Include` and `ThenInclude`, loading two navigation levels from SQL Server.
- `drop-orderby-via-cache`: `OrderBy(..., true)` followed by `EnableCache(..., false)` then `ThenByDescending(...)` preserves the secondary descending order in SQL results.
- `thread-hop-discarded-include`: `Include(..., false)` continued by `ThenInclude(...)` on a fresh thread emits no orphan `ThenInclude`.
- `thread-hop-active-include`: `Include(..., true)` continued by `ThenInclude(...)` on a fresh thread whose `[ThreadStatic]` was dirtied by a false `Include` still loads both navigation levels.
- `compat-field`: `Specification<T>.IsChainDiscarded` remains a `public static bool` field marked `[Obsolete]`.

## How to get to it (user POV)

- Build a `Specification<T>` where `Include`/`OrderBy` handles are saved and continued after other builder calls (`OrderBy`, `Include`, `EnableCache`, `WithCacheKey`) or across an `await` thread hop, then execute it through EF Core or EF6 `RepositoryBase<T>`.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #78, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/builder-chains`.

- **EF Core driver.** Make an EF Core consumer, replace its `.cs` files with `"$S"/efcore/*.cs`, then build and run:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-chains-efcore")
  rm "$C"/*.cs && cp "$S"/efcore/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

- **EF6 driver.** Same with `verify consumer` and `"$S"/ef6/*.cs`:

  ```bash
  C=$(verify consumer "$T" "$(basename "$T")-chains-ef6")
  rm "$C"/*.cs && cp "$S"/ef6/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Both drivers print identical labels and results:

  | Label | Expected result |
  |---|---|
  | `orphan-include-via-orderby` | `includes=0; rows=[Alpha:0/0, Beta:0/0]` |
  | `orphan-orderby-via-cache` | `orders=0; sqlCount=2; memCount=2` |
  | `drop-include-via-orderby` | `includes=2; rows=[Alpha:2/4, Beta:2/4]` |
  | `drop-orderby-via-cache` | `orders=2; names=[A4, A3, A2, A1, B4, B3, B2, B1]` |
  | `thread-hop-discarded-include` | `includes=0; rows=[Alpha:0/0, Beta:0/0]` |
  | `thread-hop-active-include` | `includes=2; rows=[Alpha:2/4, Beta:2/4]` |
  | `compat-field` | `present=True; public=True; static=True; type=Boolean; obsolete=True` |

- **Existing queries.** Drive the default EF Core and EF6 consumers on `$B` and `$T` and `diff` each pair of `verify run` outputs, checking both hold rows.

## Gotchas

- Remove the template's `.cs` files before copying `efcore/BuilderChains.cs` or `ef6/BuilderChains.cs` so there is only one top-level statements file (avoids CS8802).
- Each driver uses its own database (`VerifySpecificationBuilderChainsEFCore` or `VerifySpecificationBuilderChainsEF6`) and drops it in `finally`.
- EF6 disables lazy loading and proxies on `ChainContext` so unloaded navigations stay empty instead of lazy-loading during formatting.

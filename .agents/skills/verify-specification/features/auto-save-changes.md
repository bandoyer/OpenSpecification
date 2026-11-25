# Optional SaveChangesAsync (AutoSaveChanges)

Since #74, a `RepositoryBase<T>` subclass in either provider can override `protected virtual bool AutoSaveChanges => true;` to `false` so write methods stage changes on the shared `DbContext` without calling `SaveChangesAsync`. The caller commits one or more repositories' staged changes atomically by calling `SaveChangesAsync`. A tree from before #74 has no `AutoSaveChanges` property, so the runtime drivers below build only against a tree that has it.

## Sub-features

- `default-writes`: all seven write methods (`AddAsync`, `AddRangeAsync`, `UpdateAsync`, `UpdateRangeAsync`, `DeleteAsync`, `DeleteRangeAsync(IEnumerable<T>)`, `DeleteRangeAsync(ISpecification<T>)`) persist immediately when `AutoSaveChanges` is not overridden.
- `deferred-writes`: all seven write methods skip `SaveChangesAsync` when `AutoSaveChanges => false`, returning the entity/entities or `0`, and one explicit `SaveChangesAsync` persists all staged changes.
- `multi-repo-uow`: two `AutoSaveChanges => false` repositories sharing one `DbContext` commit multiple writes atomically on a single `SaveChangesAsync`.
- `failure-rollback`: a SQL Server foreign-key constraint violation during `SaveChangesAsync` throws `DbUpdateException` and leaves earlier staged writes unpersisted; disposing the `DbContext` without calling `SaveChangesAsync` also leaves SQL Server unchanged.
- `compat`: a direct `IRepositoryBase<T>` implementation and a `RepositoryBase<T>` subclass overriding write methods build against both the merge base and the branch with no new errors or warnings.

## How to get to it (user POV)

- Subclass `RepositoryBase<T>` and override `protected override bool AutoSaveChanges => false;`.
- Call `AddAsync`, `UpdateAsync`, `DeleteAsync`, and range variants across one or more repositories sharing a scoped `DbContext`, then call `SaveChangesAsync` once to commit.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #74, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/auto-save-changes`.

- **EF Core driver.** Make an EF Core consumer, replace its `.cs` files with `"$S"/efcore/*.cs`, then build and run:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-autosave-efcore")
  rm "$C"/*.cs && cp "$S"/efcore/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

- **EF6 driver.** Same with `verify consumer` and `"$S"/ef6/*.cs`:

  ```bash
  C=$(verify consumer "$T" "$(basename "$T")-autosave-ef6")
  rm "$C"/*.cs && cp "$S"/ef6/*.cs "$C/"
  verify build "$C" 2>&1
  verify run "$C"
  ```

  Both drivers print identical labels and results:

  | Label | Expected result |
  |---|---|
  | `default-add` | `returned=Alpha; db=[Alpha]` |
  | `default-add-range` | `returned=[Beta, Gamma]; db=[Alpha, Beta, Gamma]` |
  | `default-update` | `count=1; db=[Alpha-Updated, Beta, Gamma]` |
  | `default-update-range` | `count=2; db=[Alpha-Updated, Beta-Updated, Gamma-Updated]` |
  | `default-delete` | `count=1; db=[Beta-Updated, Gamma-Updated]` |
  | `default-delete-range` | `count=1; db=[Gamma-Updated]` |
  | `default-delete-range-spec` | `count=1; db=[<none>]` |
  | `deferred-add-before-save` | `returned=Deferred-Add; saveCalls=0; hasRow=False` |
  | `deferred-add-range-before-save` | `returned=[Deferred-AddRange-1, Deferred-AddRange-2]; saveCalls=0; hasRow=False` |
  | `deferred-update-before-save` | `returned=0; saveCalls=0; hasUpdated=False` |
  | `deferred-update-range-before-save` | `returned=0; saveCalls=0; hasUpdated=False` |
  | `deferred-delete-before-save` | `returned=0; saveCalls=0; stillExists=True` |
  | `deferred-delete-range-before-save` | `returned=0; saveCalls=0; stillExists=True` |
  | `deferred-delete-range-spec-before-save` | `returned=0; saveCalls=0; stillExists=True` |
  | `deferred-after-save` | `savedCount=10; saveCalls=1; db=[Deferred-Add, Deferred-AddRange-1, Deferred-AddRange-2, Seed-Update-Done, Seed-UpdateRange-1-Done, Seed-UpdateRange-2-Done]` |
  | `uow-before-save` | `countries=[Host]; cities=[<none>]` |
  | `uow-after-save` | `savedCount=3; countries=[Host-Renamed, NewCountry]; cities=[NewCity]` |
  | `failure-rollback` | `exception=DbUpdateException; countries=[<none>]; cities=[<none>]` |
  | `uncommitted-dispose` | `countries=[<none>]` |

- **Non-breaking contract and overrides.** Copy `compat/Compat.cs` and the provider's override file into consumers from `$B` and `$T`, then build both and compare `build: exit 0` and warnings:

  ```bash
  for tree in "$B" "$T"; do
    C=$(verify efcore-consumer "$tree" "$(basename "$tree")-compat-efcore")
    cp "$S/compat/Compat.cs" "$S/compat/EfCoreOverride.cs" "$C/"
    verify build "$C" 2>&1
    C6=$(verify consumer "$tree" "$(basename "$tree")-compat-ef6")
    cp "$S/compat/Compat.cs" "$S/compat/Ef6Override.cs" "$C6/"
    verify build "$C6" 2>&1
  done
  ```

- **Existing queries.** Drive the default EF Core and EF6 consumers on `$B` and `$T` and `diff` each pair of `verify run` outputs, checking both hold rows.

## Gotchas

- Remove the template's `.cs` files before copying `efcore/AutoSave.cs` or `ef6/AutoSave.cs` so there is only one top-level statements file (avoids CS8802).
- Each driver uses its own database (`VerifySpecificationAutoSaveEFCore` or `VerifySpecificationAutoSaveEF6`) and drops it in `finally`.
- `verify build` writes compiler errors to stderr and warnings/summary to stdout, so redirect `2>&1`.

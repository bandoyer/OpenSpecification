# EF6 nullable annotations

A developer whose project has nullable reference types enabled gets compiler warnings from the EF6 provider's public members. Dereferencing a result that can be null warns. Passing `null` where the provider needs a value warns. Passing `null` to an optional argument doesn't. The provider and its test project build with no warning.

## Sub-features

- `nullable-results`: dereferencing the awaited result of `GetByIdAsync`, or of `FirstOrDefaultAsync` or `SingleOrDefaultAsync` with and without a projection, warns CS8602.
- `nullable-optional-evaluator`: `WithSpecification(spec, evaluator: null)`, in both overloads, gives no warning.
- `nullable-null-specification`: `null` as the specification to `ListAsync` or to either `WithSpecification` warns CS8625.
- `nullable-id-type`: `GetByIdAsync<string?>` warns CS8714.
- `provider-warnings`: the provider builds for each framework it targets with no warning.
- `test-project-warnings`: the EF6 test project, which has nullable enabled, builds with no warning.

## How to get to it (user POV)

- Reference `OpenSpecification.EntityFramework6` from a project with `<Nullable>enable</Nullable>`, and build it.
- Build the provider or its test project from the repository.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>`.

- **Warning report.** Run `.agents/skills/verify-specification/tests/warnings.sh`. It prints three `ok:` lines and exits 0: real MSBuild warnings with and without a code, and NuGet's, the compiler's and MSBuild's other forms, each counted. A `warnings: 0` from the two recipes below counts only after this passes, since a report that misses a form reads as no warning.
- **Probes.** Run `C=$(verify consumer "$T")`, then `verify build "$C"`. Each `probe` line shows the warnings its line got. With the provider annotated, each probe's `got` matches its `an annotated provider gives`. Against a provider without annotations, every probe gets `none`. A consumer from a tree with #11's DbSet extensions also prints `DbSetProbes.cs`'s two probes, which [DbSet read extensions](./dbset-read-extensions.md) covers.
- **Provider.** Run `verify warnings "$T/src/$L.EntityFramework6/$L.EntityFramework6.csproj"`, with `L=$(verify library "$T")`. It ends with `warnings: 0` and `build: exit 0`. Add `-p:Nullable=enable` to see what nullable would report for a commit that has it off.
- **Test project.** Run `verify warnings "$T/tests/$L.EntityFramework6.Tests/$L.EntityFramework6.Tests.csproj"`. It ends with `warnings: 0` and `build: exit 0`.

## Gotchas

- The consumer targets net10.0 only, so its probes see the provider's net10.0 build, or its net9.0 build for a tree from before #58. The provider's warnings for each framework come from `verify warnings`.
- `verify build` also lists the provider's own warnings, since it builds the provider the consumer references. A warning under `trees/` is the library's, from the provider or the core library it references, and one under `consumers/` is the consumer's.
- A probe's line number moves when its file changes. `verify build` reads the numbers from the file each time.

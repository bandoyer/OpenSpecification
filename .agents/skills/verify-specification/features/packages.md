# Packages

A developer installs the library from NuGet: `OpenSpecification`, `OpenSpecification.EntityFrameworkCore` and `OpenSpecification.EntityFramework6`, each at the version its project file names. Each package holds its assembly and XML docs for each target framework, and its nuspec names the author, copyright, license, project and repository URLs, readme and release notes. #47 renamed the packages from `Ardalis.Specification*`, so a project moving from 9.x swaps its package references and its using directives, and an app can load Ardalis.Specification 9.3.1 and OpenSpecification side by side.

## Sub-features

- `pack`: the packages `verify pack` writes, with their nuspecs and file lists.
- `migration`: a project written against `Ardalis.Specification.EntityFrameworkCore` 9.3.1 from nuget.org, moved to the feed's `OpenSpecification.EntityFrameworkCore` by CHANGELOG.md's two migration steps, and with only the first step done.
- `side-by-side`: an app that references the feed's `OpenSpecification.EntityFrameworkCore` and a class library of its own that references `Ardalis.Specification` 9.3.1 from nuget.org, evaluating one specification from each in one process.

## How to get to it (user POV)

- Run `dotnet add package OpenSpecification.EntityFrameworkCore`, or `OpenSpecification.EntityFramework6`, which brings `OpenSpecification` with it, then import `OpenSpecification` and the provider's namespace.
- From 9.x, replace the `Ardalis.Specification*` package reference with the `OpenSpecification*` one of the same provider, then replace `Ardalis.Specification` with `OpenSpecification` in the using directives.

## Driving it with verify

Preconditions:

- `verify doctor` passes, and nuget.org is reachable.
- `T` is a tree from `verify tree <commit>`, and `F=$(verify pack "$T" | head -1)` is its feed. `verify package-consumer "$T" <template> [<name>]` copies `package-consumer-templates/<template>/` into a consumer, `<name>` defaulting to the template's name.

- **Pack.** `verify pack "$T"` prints the feed folder, then one `.nupkg` and one `.snupkg` for each package. To read a package's nuspec and file list:

  ```bash
  for p in "$F"/*.nupkg; do echo "== $p"; unzip -p "$p" '*.nuspec'; unzip -Z1 "$p"; done
  ```

  `unzip -p "$p" LICENSE` and `unzip -p "$p" readme-nuget.md` print the two text files the packages carry. An assembly's version is in its name as the runtime sees it: build any package consumer that loads it and print `typeof(<a type in it>).Assembly.FullName`, as the side-by-side app does.

- **Migration.** Make a package consumer from the template `package-consumer-templates/migration/`, a 9.x project that lists rows through a `RepositoryBase<T>` subclass on EF Core's in-memory database:

  ```bash
  C=$(verify package-consumer "$T" migration)
  verify build "$C"; verify run "$C"
  ```

  It prints `rows: Albania, Austria` and `library: Ardalis.Specification 9.3.1.0`. Then do only the first migration step, and build:

  ```bash
  sed -i 's/Include="Ardalis.Specification.EntityFrameworkCore" Version="9.3.1"/Include="OpenSpecification.EntityFrameworkCore" Version="10.0.0"/' "$C/Consumer.csproj"
  verify build "$C" 2>&1
  ```

  It ends `build: exit 1`, with `error CS0246` for the namespace `Ardalis`. Then the second step:

  ```bash
  sed -i 's/\bArdalis\.Specification\b/OpenSpecification/g' "$C/Program.cs"
  verify build "$C"; verify run "$C"
  ```

  It prints `rows: Albania, Austria` and `library: OpenSpecification 10.1.0.0`.

- **Side by side.** Make a package consumer from the template `package-consumer-templates/side-by-side/`, an app with a class library of its own in `Legacy/`:

  ```bash
  C=$(verify package-consumer "$T" side-by-side)
  verify build "$C"; verify run "$C"
  ```

  `verify build` ends `build: exit 0`, with no CS0433 and no MSB3277 among its warnings. The run prints `legacy: Albania, Austria`, `open: Norway`, and the two assemblies, `Ardalis.Specification, Version=9.3.1.0` and `OpenSpecification, Version=10.1.0.0`, each with `Culture=neutral, PublicKeyToken=null`.

## Gotchas

- Each package consumer restores into its own `packages/` folder. A project restored with the default `~/.nuget/packages` would keep the first `OpenSpecification` 10.1.0 it saw there, so a later pack under the same version would never reach it, and an unpublished package would sit in the user's cache.
- `verify run` takes the shared SQL Server lock, though these consumers use no database. It only waits on a test run.
- The side-by-side app's `Compile Remove="Legacy/**"` keeps the app from compiling the library's files a second time, since the SDK globs every `.cs` file under the project's folder.

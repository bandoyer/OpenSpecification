# Target frameworks

A developer's project gets the build of each package that matches its framework. Since #58, `OpenSpecification` builds for net10.0, net9.0, net8.0 and netstandard2.0, `OpenSpecification.EntityFrameworkCore` for net10.0, net9.0 and net8.0, and `OpenSpecification.EntityFramework6` for net10.0, net9.0, net8.0 and net472. The EF Core provider's build for each framework depends on that framework's EF Core: 10.0.x on net10.0, 9.0.x on net9.0 and 8.0.x on net8.0. So a net10.0 project gets EF Core 10, and a net9.0 or net8.0 project keeps EF Core 9 or 8.

## Sub-features

- `framework-builds`: the `lib/<framework>/` folders in each package `verify pack` writes, and the EF Core dependency group for each framework in the EF Core package's nuspec.
- `package-consumers`: the EF Core and EF6 consumers built against the feed's provider package, in place of the provider project, on net10.0, net9.0 or net8.0, with the package build and EF Core version each restore picks.

## How to get to it (user POV)

- Run `dotnet add package OpenSpecification.EntityFrameworkCore`, or `OpenSpecification.EntityFramework6`, in a project on any framework the package targets. NuGet picks the package's build for that framework, and EF Core's version from that build's dependency group.

## Driving it with verify

Preconditions:

- `verify doctor` passes, and nuget.org is reachable.
- `T` is a tree from `verify tree <commit>`, and `F=$(verify pack "$T" | head -1)` is its feed.

- **Framework builds.** List each package's builds and the dependency groups its nuspec declares:

  ```bash
  for p in "$F"/*.nupkg; do echo "== $p"; unzip -Z1 "$p" | grep '^lib/'; unzip -p "$p" '*.nuspec' | grep -E '<group|<dependency '; done
  ```

  Each `lib/<framework>/` folder holds the assembly and its `.xml` docs. The EF Core package's `<group targetFramework="net10.0">` names `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational` at the version `Directory.Packages.props` gives net10.0, and so on for net9.0 and net8.0.

- **Package consumers.** Make a consumer that references the feed's provider package, for the framework you name, then build it, run it, and read what its restore picked:

  ```bash
  C=$(verify efcore-consumer --package --framework net10.0 "$T" efcore-package-net10)
  verify build "$C"; verify run "$C"
  jq -r '.targets | to_entries[] | .key as $fw | .value | to_entries[]
    | select(.key | test("^(OpenSpecification|Microsoft\\.EntityFrameworkCore|EntityFramework)(\\.[A-Za-z0-9]+)?/"))
    | "\($fw) \(.key) \((.value.compile // {}) | keys | join(" "))"' "$C/obj/project.assets.json"
  ```

  `verify build` ends `build: exit 0`, and `verify run` prints the same lines as the consumer built against the provider project. The `jq` lines name each package with the version the restore picked and the assembly it compiles against, such as `net10.0 OpenSpecification.EntityFrameworkCore/10.0.0 lib/net10.0/OpenSpecification.EntityFrameworkCore.dll` and `net10.0 Microsoft.EntityFrameworkCore/10.0.14 lib/net10.0/Microsoft.EntityFrameworkCore.dll`. `verify consumer --package` does the same for the EF6 provider, whose restore also picks `EntityFramework`.

  For net9.0 or net8.0, pass that framework and another name. The restore picks the package's build for that framework and its EF Core. `verify run` needs that framework's runtime, so on a machine with only the .NET 10 and 9 runtimes, a net8.0 consumer stops after `verify build` and the `jq` lines.

## Gotchas

- The EF Core consumer also references `Microsoft.EntityFrameworkCore.SqlServer`, at the EF Core version the tree's `Directory.Packages.props` gives the consumer's framework. It depends on EF Core at its own version, so the EF Core version in `project.assets.json` is the higher of the two. The nuspec's dependency group is the package's own claim, and the framework builds recipe reads it.
- A tree from before #58 has no net10.0 builds. A net10.0 consumer of it compiles against the net9.0 build, and its `Microsoft.EntityFrameworkCore.SqlServer` takes the tree's SQL Server version, 9.0.8.
- `--package` needs the tree's feed, so run `verify pack "$T"` first. The consumer restores into its own `packages/` folder, as each package consumer does, so a feed packed again under the same version reaches it.

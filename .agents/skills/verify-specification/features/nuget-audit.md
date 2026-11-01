# NuGet audit

A contributor who builds the solution sees the NuGet audit's warnings, NU1901 to NU1904, one for each package a project resolves with a known advisory. The .NET 10 SDK audits transitive packages for net10.0 projects, so a test, benchmark or sample project also gets the advisories of packages it never names, such as SSH.NET through Testcontainers.MsSql. Since #61 the solution reports only AutoMapper 14.0.0's NU1903, GHSA-rvv3-g6hj-g44x, in the three sample apps, whose `[14.0.0]` pin is deliberate. The three packages report none.

## Sub-features

- `project-audit`: the audit warnings one project builds with, for every framework it targets.

## How to get to it (user POV)

- Run `dotnet build OpenSpecification.sln`, or build one project, and read the `warning NU190x` lines.

## Driving it with verify

Preconditions:

- `verify doctor` passes, nuget.org is reachable, and `tests/warnings.sh` passes.
- `T` is a tree from `verify tree <commit>`.

- **Project audit.** Build each test, benchmark and sample project, and keep its audit lines:

  ```bash
  for p in "$T"/tests/*/*.csproj "$T"/samples/*/*.csproj; do
    echo "== ${p#"$T"/}"; verify warnings "$p" | grep -E ' NU190[1-4] |^build: exit'
  done
  ```

  Each project ends `build: exit 0`. Only the three `Sample.App` projects print a `NU190x` line, AutoMapper 14.0.0's NU1903 GHSA-rvv3-g6hj-g44x. The packages' own dependencies are the [target frameworks](./target-frameworks.md) feature's framework builds recipe, which reads each nuspec's dependency groups.

## Gotchas

- The audit reads nuget.org's vulnerability data when it restores, so a new advisory can turn a clean commit's report red the day it is published. Compare two commits on the same day.
- An advisory that `NuGetAuditSuppress`, `NoWarn` or `NuGetAudit` set to false hides prints no line, though the package is still vulnerable. Before calling a project clean, grep the tree's `Directory.Packages.props`, `Directory.Build.props` files and the project's file for those three.

# Specification verification map

This folder lists what `verify` drives in the Specification library, one file per feature. Read this index first, then use the feature's file as the recipe.

## Baseline preconditions

- `verify doctor` passes.
- Each commit under test has a tree from `verify tree <commit>`, and each tree has a consumer from `verify consumer`, or from `verify efcore-consumer` for an EF Core feature.
- SQL Server is the spec-sql container from CONTRIBUTING.md, or the server `SPECIFICATION_TEST_SQL_SERVER` names.

## Driving conventions

- Drive a commit through its tree. The working copy's uncommitted edits never reach a tree.
- Compare a branch with `git merge-base origin/HEAD HEAD`, since `main` moves while a branch waits.
- Run the consumer through `verify run`, which holds the shared lock the test suite also takes.

## Proof and skip reporting

- A proof names the tree's commit and the command, and keeps the command's whole output.
- A comparison shows that both outputs hold rows before it calls them equal.
- Report a recipe you couldn't drive with its command and what stopped it. A different recipe can't stand in for it.

## Feature entry contract

Each feature file starts with an H1 title and a paragraph on what a developer using the library sees. Four H2s follow, in this order: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify`, and `Gotchas`.

## Features

- [EF6 nullable annotations](./ef6-nullable-annotations.md): the warnings a nullable-enabled caller gets from the EF6 provider's public members, and the warnings the provider and its test project build with.
- [EF6 ordering](./ef6-ordering.md): ordering a query through the EF6 provider, a spec with two order chains, and the `OrderEvaluator`'s in-memory `Evaluate`.
- [EF6 repository queries](./ef6-repository-queries.md): querying SQL Server through the EF6 provider's `RepositoryBase<T>` and `DbSet` extensions.
- [DbSet read extensions](./dbset-read-extensions.md): `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `CountAsync`, `AnyAsync` and the projection overloads on a `DbSet`, in both providers, EF Core's `AsAsyncEnumerable`, and where a `default` or `null` argument binds.
- [EF6 include chains](./ef6-include-chains.md): two `Include` and `ThenInclude` chains in one specification, as a repository loads them.
- [EF6 include strings](./ef6-include-strings.md): navigations loaded by include strings, the SQL EF6 builds for them, and a custom evaluator list holding one include evaluator.
- [EF Core context-factory repository](./efcore-context-factory-repository.md): `AsAsyncEnumerable`, `SingleOrDefaultAsync` and `FirstOrDefaultAsync` through `ContextFactoryRepositoryBaseOfT`, and whether each context its factory made is disposed, whether the caller reads every row, stops early, or never starts, and whether two matches throw.
- [Projected AsAsyncEnumerable](./projected-async-enumerable.md): streaming a projection specification with `AsAsyncEnumerable` through both EF Core repository bases and a `DbSet`, with a `break`, a cancel, a `PostProcessingAction`, a missing `Select` and a `null`, which overload a call binds to before and after #16, and EF6's `NotImplementedException`.
- [Packages](./packages.md): the packages `verify pack` writes and their metadata, a 9.x project moved to OpenSpecification by CHANGELOG.md's migration steps, and an app that loads Ardalis.Specification 9.3.1 and OpenSpecification side by side.
- [Target frameworks](./target-frameworks.md): the frameworks each package builds for, the EF Core version behind each of the EF Core provider's builds, and a project on net10.0, net9.0 or net8.0 that references the feed's provider packages.
- [NuGet audit](./nuget-audit.md): the NU1901 to NU1904 advisories each test, benchmark and sample project builds with, per project.
- [Query builder](./query-builder.md): reading `Query` through the specification classes and through `ISpecification<T>`, a specification class that implements `ISpecification<T>` without it, and the XML docs of the classes' `Query`.
- [Paged results](./paged-results.md): `PagedListAsync` and `ToPagedListAsync` in both providers since #59, through each repository base and `DbSet`, with projections, clamped filters, settings, the SQL each call sends, the calls it refuses, a JSON round trip, and a consumer's own `Pagination` beside the library's namespace.
- [Sample App3](./sample-app3.md): App3's `GET /customers` page and pagination JSON, and which paging classes App3 defines itself.

Of the EF Core provider, the map covers only the context-factory repository's `AsAsyncEnumerable`, `SingleOrDefaultAsync` and `FirstOrDefaultAsync`, both repository bases' projected `AsAsyncEnumerable` and `PagedListAsync`, and the `DbSet` extensions. It doesn't cover the rest of that provider, the core library's in-memory evaluation, or the sample apps other than App3's customers page yet. A run that changes one adds its file here.

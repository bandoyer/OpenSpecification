# Paged results

Since #59, a developer gets one page of a specification's rows, with the page's details, in one call. The core library's `OpenSpecification.Paging` namespace holds `IPagingFilter`, `PagingFilter`, `PaginationSettings`, `Pagination` and `PagedResult<T>`. Both providers' repository bases have `PagedListAsync`, and their `DbSet` extensions have `ToPagedListAsync`, for an `ISpecification<T>` and for an `ISpecification<T, TResult>`. Each counts the rows the specification's criteria match, then runs the full query for the page the filter asks for, clamped to the pages that exist. A tree from before #59 has none of this, so the drivers below build only against a tree that has it.

## Sub-features

- `page`: page 2 of 25 matches, ten to a page, through EF Core's `RepositoryBase<T>` and `ContextFactoryRepositoryBaseOfT`, EF6's `RepositoryBase<T>`, and each provider's `DbSet`.
- `projected-page`: the `ISpecification<T, TResult>` overloads with a `Select`, and with a `SelectMany`, whose total counts the flattened rows in EF Core, and which EF6 refuses.
- `clamping`: a page past the last, a page of 0, -1 or null, a page size of 0, null or past the limit, and a specification nothing matches.
- `settings`: `PaginationSettings` passed to `ToPagedListAsync`, a repository subclass that overrides `PaginationSettings`, and the settings' argument checks.
- `count-and-page-sql`: the count and page queries each call sends, for a specification with an `Include`, an `OrderBy` and a `PostProcessingAction`, and the page's rows without the post-processing.
- `refused`: a specification with its own `Take` or `Skip`, and a null filter, each refused before any query.
- `json`: a `PagedResult<string>` written by System.Text.Json's web defaults, and read back by System.Text.Json and by Newtonsoft.Json, with no serializer attributes on the types.
- `name-clash`: a consumer's own `Pagination` and `PaginationSettings`, imported in the same file as `OpenSpecification`, with and without a `PagedListAsync` call.

## How to get to it (user POV)

- Write a specification with criteria and an `OrderBy`, then call `repository.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 })`, or `dbContext.Countries.ToPagedListAsync(spec, filter)`.
- Bind a web request's query string to a class of your own that implements `IPagingFilter`, and pass it as the filter.
- Serialize the `PagedResult<T>` as a web API's response.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>` of a commit with #59, `B` is a tree of `$(git merge-base origin/HEAD HEAD)`, and `S` is `.agents/skills/verify-specification/paged-results`.

- **EF Core driver.** Make an EF Core consumer, swap its files for the driver, add Newtonsoft.Json for the `json` line, then build and run it:

  ```bash
  C=$(verify efcore-consumer "$T" "$(basename "$T")-paged-efcore")
  rm "$C"/*.cs && cp "$S"/efcore/*.cs "$C/"
  sed -i 's|\(<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer"[^>]*/>\)|\1\n    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />|' "$C/Consumer.csproj"
  verify build "$C" 2>&1
  verify run "$C"
  ```

- **EF6 driver.** The same with `verify consumer` and `"$S"/ef6/*.cs`, and no Newtonsoft.Json line. Its labels are the EF Core driver's, less the `factory-` lines and `json`, plus `repository-selectmany-first`.

  Each driver prints one `label: result` line per call. A page reads `[<items>] (<n> items); Page <p>, PageSize <s>, TotalItems <t>, TotalPages <n>, HasPrevious <b>, HasNext <b>`. An exception reads `<type> (parameter <name>): <message>`. A line run with SQL is followed by `  sql commands: <n>`, then one `  sql: <text>` line per command, in the order sent. The seed is 25 countries named `Match 01` to `Match 25`, each with two cities, among five named `Other 1` to `Other 5`, inserted out of name order. Since #59 the drivers print:

  | Labels | Result |
  |---|---|
  | `*-page-2`, `*-select-page-2` | `Match 11` to `Match 20`; `Page 2, PageSize 10, TotalItems 25, TotalPages 3, HasPrevious true, HasNext true` |
  | `*-selectmany-last`, EF Core | `(2 items)`; `Page 5, PageSize 12, TotalItems 50, TotalPages 5, HasPrevious true, HasNext false` |
  | `*-selectmany-*`, EF6 | `NotSupportedException` with a message that names SelectMany and says to use a Select, and `sql commands: 0` |
  | `clamp-page-99` | `Match 21` to `Match 25`; `Page 3`, `HasNext false` |
  | `clamp-page-0`, `clamp-page-minus-1`, `clamp-page-null` | `Match 01` to `Match 10`; `Page 1`, `HasPrevious false` |
  | `clamp-size-0`, `clamp-size-null` | `PageSize 10`, `TotalPages 3` |
  | `clamp-size-1000` | all 25; `PageSize 100, TotalPages 1` |
  | `no-match` | `<none>`; `Page 1, PageSize 10, TotalItems 0, TotalPages 1, HasPrevious false, HasNext false` |
  | `settings-*-size-null` | `PageSize 5, TotalPages 5` |
  | `settings-*-size-50` | `PageSize 20, TotalPages 2` |
  | `settings-invalid-0-10`, `settings-invalid-10-5` | `ArgumentOutOfRangeException` |
  | `*-include-order-post` | `Match 11 (2 cities)` first, to `Match 20 (2 cities)`; two SQL commands, a count with no join to the cities and no `ORDER BY`, then the page query with both |
  | `*-spec-take-5`, `*-spec-skip-5` | `ArgumentException (parameter specification)`, a message that says to remove Skip and Take because the filter sets the page, and `sql commands: 0` |
  | `*-null-filter` | `ArgumentNullException (parameter filter)` and `sql commands: 0` |
  | `json` | `{"items":["Match 11",...,"Match 20"],"pagination":{"page":2,"pageSize":10,"totalItems":25,"totalPages":3,"hasPrevious":true,"hasNext":true}}`, then `system-text-json:` and `newtonsoft:` lines that each show the same page as `repository-select-page-2` |

  The `selectmany-last` row names no items: the last page holds the two flattened rows past 48, and which two depends on the order SQL Server returns cities within a country. Read its count and pagination.

- **Name clash.** Copy `name-clash/`'s files into EF Core consumers, then build each with its errors:

  ```bash
  C=$(verify efcore-consumer "$B" "$(basename "$B")-name-clash")
  cp "$S"/name-clash/OwnPaging.cs "$S"/name-clash/UsesBoth.cs "$C/"
  verify build "$C" 2>&1
  C=$(verify efcore-consumer "$T" "$(basename "$T")-name-clash")
  cp "$S"/name-clash/OwnPaging.cs "$S"/name-clash/UsesBoth.cs "$S"/name-clash/CountryRequest.cs "$S"/name-clash/PagedCall.cs "$C/"
  verify build "$C" 2>&1
  C=$(verify efcore-consumer "$T" "$(basename "$T")-clash-control")
  cp "$S"/name-clash/OwnPaging.cs "$S"/name-clash/Clash.cs "$C/"
  verify build "$C" 2>&1
  ```

  Since #59, the first two end `build: exit 0` with the same warnings, and the control ends `build: exit 1` with `error CS0104` on `Clash.cs`'s line marked `CLASH`. A paging type in the root namespace would fail the second with CS0104 on `UsesBoth.cs` and `PagedCall.cs`.

- **Existing queries.** Drive the consumers of [EF6 repository queries](./ef6-repository-queries.md), [DbSet read extensions](./dbset-read-extensions.md), [EF Core context-factory repository](./efcore-context-factory-repository.md) and [Projected AsAsyncEnumerable](./projected-async-enumerable.md) on `$B` and `$T`, and `diff` each pair of `verify run` outputs.
- **Package dependencies.** Run `verify pack` on `$B` and `$T`, as [Packages](./packages.md) shows, then compare the core package's dependency groups and look for serializer attributes:

  ```bash
  for tree in "$B" "$T"; do unzip -p "$tree"/feed/OpenSpecification.10.*.nupkg OpenSpecification.nuspec | sed -n '/<dependencies>/,/<\/dependencies>/p'; done
  grep -rnE '\[(Json|DataMember|DataContract|Serializable)' "$T/src/OpenSpecification/Paging/" || echo "no serializer attributes"
  ```

## Gotchas

- Swap the template's files out before the first build. A consumer that holds both the template's `Program.cs` and the driver has two files of top-level statements and fails with CS8802.
- Each driver uses a database of its own, `VerifySpecificationPagedEFCore` or `VerifySpecificationPagedEF6`, and drops it at the end. A driver killed midway leaves it behind, and its next run drops it first.
- EF6 refuses `Skip` on input it can't see as sorted, and a specification can't order the rows a `SelectMany` flattens, so EF6's paged methods refuse a `SelectMany` specification before any query. An EF6 `*-selectmany-*` line whose `NotSupportedException` names Skip, or that shows a SQL command, means the provider sent a query before refusing.
- `verify build` prints the errors on stderr and the rest on stdout, so save both with `2>&1`.
- The drivers and the name-clash files import `OpenSpecification.Paging`, which exists only since #59. They don't build against `$B`, except `OwnPaging.cs` and `UsesBoth.cs`.

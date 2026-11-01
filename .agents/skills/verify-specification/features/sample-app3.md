# Sample App3

Sample App3 is the web API sample with its own repository base, an extra evaluator, and AutoMapper projections. Its `GET /customers` returns one page of customers and the page's details. Before #59 it pages with its own `Pagination`, `PaginationSettings` and `PagedResponse<T>`, and its JSON has `data` and `pagination`, with `startItem` and `endItem`. Since #59 it uses the library's `OpenSpecification.Paging` types, and its JSON has `items` and `pagination`, with the six details `Pagination` holds. It seeds two customers, `Customer1` and `Customer2`, each time it starts.

## Sub-features

- `customers-page`: `GET /customers?page=<p>&pageSize=<s>` returns that page of customers, ordered by id, and its pagination.
- `paging-types`: which paging classes App3 defines itself, and what its `BaseFilter` implements.

## How to get to it (user POV)

- Run `samples/OpenSpecification.Sample.App3` and open `/customers?page=1&pageSize=1`, or its Swagger UI at `/swagger`.

## Driving it with verify

Preconditions:

- `verify doctor` passes.
- `T` is a tree from `verify tree <commit>`.

- **Customers page.** Run App3 from the tree on a loopback port, against a database of its own on spec-sql, while holding the shared lock. Request two pages, then stop it:

  ```bash
  SQL=${SPECIFICATION_TEST_SQL_SERVER:-'Server=localhost,1433;User Id=sa;Password=P@ssW0rd!;TrustServerCertificate=True'}
  ConnectionStrings__DbConnection="$SQL;Database=VerifySpecificationSampleApp3" \
  flock ~/.cache/specification-spec-sql.lock bash -c '
    dotnet run --project "$0/samples/OpenSpecification.Sample.App3" --no-launch-profile --urls http://127.0.0.1:5291 > "$0/app3.log" 2>&1 &
    app=$!
    for i in $(seq 120); do curl -sf -o /dev/null http://127.0.0.1:5291/customers && break; sleep 1; done
    curl -s "http://127.0.0.1:5291/customers?page=1&pageSize=1"; echo
    curl -s "http://127.0.0.1:5291/customers?page=2&pageSize=1"; echo
    kill "$app"; wait "$app"
  ' "$T"
  ```

  Since #59, the first line holds `Customer1` alone under `items`, and `"pagination":{"page":1,"pageSize":1,"totalItems":2,"totalPages":2,"hasPrevious":false,"hasNext":true}`. The second holds `Customer2`, with `"page":2`, `"hasPrevious":true` and `"hasNext":false`. Before #59, the customer is under `data`, and `pagination` also has `startItem` and `endItem`.

- **Paging types.** List App3's files and its filter's declaration:

  ```bash
  ls "$T/samples/OpenSpecification.Sample.App3"
  grep -n 'record BaseFilter\|class BaseFilter' "$T/samples/OpenSpecification.Sample.Domain/Filters/BaseFilter.cs"
  ```

  Since #59, there's no `Pagination.cs`, `PaginationSettings.cs` or `PagedResponse.cs`, and `BaseFilter` implements `IPagingFilter`.

## Gotchas

- App3 drops and creates the database its connection string names each time it starts, so the recipe's `ConnectionStrings__DbConnection` names `VerifySpecificationSampleApp3`, never a test fixture's database. The run leaves that database behind, and the next start drops it.
- `--no-launch-profile` keeps `launchSettings.json`'s ports and `Development` environment out of the run. App3 logs that it can't find an HTTPS port, and serves plain HTTP on the loopback port.
- The first `dotnet run` builds App3 inside the tree, and can take a minute. The loop waits up to two.
- `app3.log` in the tree holds App3's own output, the SQL it logs included.

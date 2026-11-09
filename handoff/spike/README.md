# Compiled specification spike (#17)

A throwaway engine for the design in [../proposal-combined.md](../proposal-combined.md). It answers two questions: does the engine work, and does a compiled spec run as fast as a hand-written `EF.CompileAsyncQuery`? It isn't the code that ships, and it goes away with the `handoff/compiled-queries` branch.

It covers `QueryArg<T>`, variants from `bool`, enum and `HasValue` reads, one to three arguments, list, first and count, includes, one projection, one delegate per model, and the capture refusals. It leaves out parameterized `Search`, `Skip` and `Take`, paged and synchronous results, and the generated overloads.

It runs on SQLite in memory, so it needs no database server.

```bash
# Correctness checks: each spec normally and compiled, and the refusals
dotnet run --project handoff/spike

# Benchmarks (Release only)
dotnet run --project handoff/spike -c Release -- bench --filter '*'
```

On the home machine, the same checks and benchmarks can move to SQL Server by changing the connection in `Model.cs`.

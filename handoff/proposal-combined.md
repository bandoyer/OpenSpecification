# Proposal: compiled specifications (#17), revision 2

Written 2026-11-09 by the chief thread. Revision 1 went to Sol (gpt-6.1-sol) and Muse (muse-wonder-full-1.0) for review. This revision folds in their findings and the results of a throwaway engine in [spike/](spike/). It's still a proposal. Dan confirms the public API before any implementation run starts, under `skillflow.toml`'s `never-delegate` rule.

## The goal

OpenSpecification is a public fork of Ardalis.Specification on nuget.org. This feature is for every developer who uses the library. It should be feature-rich, and speed comes first, since speed is the only reason to use a compiled query. The bar is a compiled spec that costs what a hand-written `EF.CompileAsyncQuery` costs.

The original request, ardalis/Specification#547, asked whether the EF Core package could support compiled queries. fiseni's reply set three requirements: strongly typed delegates, delegates built at runtime, and only simple scalar parameters. This design meets all three.

## What changed since revision 1

- `Param<T>` and plain `bool` and enum variants became one type, `QueryArg<T>`. How the constructor uses an argument decides whether it's a SQL parameter or a shape variant. Every variant now has a marker with an identity, which answers both reviewers' finding that a plain `bool` can't be told apart from an unrelated captured `bool`.
- `HasValue` on any argument is a variant, so an optional filter needs no separate `bool` argument.
- A handle's type names only the result and the argument types, so projected and plain handles never collide. Handles take a plain `DbContext`.
- The factory runs for every variant combination when `Create` is called, not lazily.
- EF's delegate takes only the arguments a shape's query uses.
- The safety claim is narrower. See "Contract and limits".
- A spike engine ran 19 correctness checks and a benchmark. See "Evidence".

## The design

### `QueryArg<T>`

A readonly struct in the core package, so domain projects with no EF reference can use it. It converts implicitly from `T` and to `T`.

```csharp
public sealed class CustomerSearchSpec : Specification<Customer>
{
    public CustomerSearchSpec(QueryArg<int> regionId, QueryArg<string?> namePrefix, QueryArg<CustomerSort> sort)
    {
        Query.Where(c => c.RegionId == regionId);
        if (namePrefix.HasValue) Query.Search(c => c.Name, namePrefix);
        if (sort == CustomerSort.Newest) Query.OrderByDescending(c => c.CreatedOn);
        else Query.OrderBy(c => c.Name);
    }
}
```

On the normal path a `QueryArg<T>` holds a value, and existing call sites such as `new CustomerSearchSpec(3, "Sm%", CustomerSort.Name)` compile unchanged. While the library builds a compiled query, it holds a marker instead: a reference to an object that records the registration, the argument's slot and its type. `default(QueryArg<T>)` is a concrete `default(T)`, never slot 0. In marker mode, `Equals`, `GetHashCode` and `ToString` throw, so nothing can inspect a marker without the library seeing it.

Specs that don't use `QueryArg` don't change.

### How an argument's use decides its role

- **Used inside a query expression, it becomes a SQL parameter.** The library finds `Convert(closure.arg)` and `closure.arg.Value`, reads the closure fields (never property getters), checks the marker belongs to this registration, and replaces the node with a typed parameter.
- **Read in the constructor, a `bool`, an enum or `HasValue` picks the shape.** The library runs the factory once per combination when `Create` is called. Each read returns the value for the combination being explored, and the library records the decision tree. At call time a compiled selector maps the arguments to a shape index.
- **Any other read in the constructor is refused,** such as `if (minimum < 0)`, `var next = id + 1` or `$"%{term}%"`.

Variant rules:

- An enum's domain is its defined values, with aliases removed. A `[Flags]` enum is refused. A value no shape covers throws `ArgumentOutOfRangeException` at call time.
- Nullable `bool` and enum variants include null.
- A spec may make at most 64 shapes.
- The constructor must read its arguments in the same order on every run with the same values. The library checks this and refuses a spec that doesn't.

### The handle

```csharp
public static readonly CompiledSpecification<Customer, int, string?, CustomerSort> Search =
    CompiledSpecification.Create<Customer, int, string?, CustomerSort>(
        (regionId, namePrefix, sort) => new CustomerSearchSpec(regionId, namePrefix, sort));

var rows = await Search.ToListAsync(dbContext, 3, "Sm%", CustomerSort.Name, ct);
```

- `CompiledSpecification<TResult, T1..Tn>` names the result type and the call's argument types. A projected spec returns its `TResult`, and a plain spec returns its entity. `Create<T, TResult, T1..Tn>` takes a factory returning `ISpecification<T, TResult>`, and `Create<T, T1..Tn>` takes one returning `ISpecification<T>`. C# can also infer the type arguments from an explicitly typed lambda.
- Handles take a plain `DbContext`. Sol's and Muse's probes proved a `DbContext`-typed compiled delegate works with derived contexts on EF Core 8, 9 and 10, so a repository that holds a plain `DbContext` can call a handle directly.
- Results: `ToListAsync`, `AsAsyncEnumerable`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `CountAsync`, `AnyAsync` and `PagedListAsync`, with synchronous forms. The spike built the first four. `CountAsync` and `AnyAsync` evaluate the criteria only, and a paged `SelectMany` counts the flattened rows, as the normal path does.
- `Warm(DbContext)` builds every shape for that context's model, so a startup test can surface refusals and translation errors.
- `Shapes` lists the shapes, for logs and tests.
- The overloads are generated up to EF's limit: 15 SQL parameters for list forms and 14 for single-result forms, which also take a `CancellationToken`. Muse measured both limits on EF Core 8, 9 and 10. Arguments that only pick a shape don't count against the limit, since EF never receives them.

### Building a shape

1. On the first call for a shape, operation and model, run the default `SpecificationEvaluator` over `context.Set<T>()`. This keeps includes working, which a stand-in provider can't do, and keeps the results equal to the normal path.
2. Replace the query root with `context.Set<T>()` on a context parameter, and replace each argument marker with its parameter.
3. Refuse anything left that could differ between calls (see "Refusals").
4. Compile with `EF.CompileAsyncQuery`, passing only the arguments this shape uses. A small adapter keeps every shape of one handle on the same delegate type.
5. Cache the delegate per model. EF throws when a compiled delegate meets a second model, which both reviewers proved. The cache publishes the last model and its delegate as one object, so the usual call is one reference comparison, and keeps the rest in a `ConditionalWeakTable`.

### Refusals

`CompiledSpecificationNotSupportedException`, thrown at `Create` or on the first build of a shape. The message names the spec, the operator and the lambda. Messages from the spike:

```text
CustomersForTenantLocalSpec, Where: captured value 'tenantId' in c => ((c.RegionId == r) AndAlso (c.TenantId == value(...).tenantId)).
CustomersForTenantSpec, Where: static member TenantContext.CurrentTenantId in c => (...).
NextCustomerSpec, Where: arithmetic on an argument ((id + 1)). Compute the value before the call in c => (c.Id == (id + 1)).
CustomersAboveIdSpec: The constructor reads argument 'm' (int). Only bool and enum arguments, or HasValue on any argument, can choose a compiled query's shape. ...
CustomersWithPermissionSpec: Permissions is a [Flags] enum, so its combinations can't be listed. Use it only inside a query expression.
PostProcessedSpec: a compiled specification can't have a PostProcessingAction.
The factory captures values. They would be frozen into the compiled query. Pass them as arguments instead.
TwoRegionSpec: a QueryArg with a fixed value is captured (...second). Its value would be frozen into the compiled query. ...
```

Also refused: an argument type that isn't a scalar, its nullable form, or an array or `List<T>` of one; `Items`; a shared-type entity; and a custom evaluator.

Allowed as fixed values: literals, `static readonly` scalar fields, `EF.Functions`, and a fixed `Search` term, which `SearchExtension` already holds in a private record.

### Parameterized `Search`, `Skip` and `Take`

Both reviewers found these need new storage, not just overloads. `SearchExpressionInfo` requires a non-empty string, `Skip` and `Take` are `int` properties, and `PaginationEvaluator` decides from the values whether to add the operators at all. The plan:

- Store the `QueryArg` alongside the existing concrete properties, and carry it through cloning, the EF Core and EF6 evaluators, the in-memory evaluator, and the paging checks.
- A symbolic `Skip` or `Take` always emits its operator.
- The handle checks real values on every call that the normal path's constructor would have checked: an empty search term, and a negative `Take`, which today means no limit.
- Paged results synthesize their own `Skip` and `Take` parameters from the paging filter.

The spike doesn't cover this. It's the largest piece of work left.

### Other rules

- A `PostProcessingAction` is refused, since dropping it would change results.
- `Items` is refused, since a custom evaluator could read it.
- Only the default evaluator is supported in version one. An evaluator can read request state when it runs, and running it once would freeze that.
- No repository methods in version one. Repositories call handles with their `DbContext`.

## Contract and limits

Revision 1 claimed markers closed both of #17's wrong-result risks. Both reviewers showed that was too strong. A constructor that branches on outside state, such as `if (FeatureFlags.OnlyActive)`, leaves no trace in the expression tree. The library can't detect it, and the compiled query keeps the shape from when it was created. The spike shows this: after the flag changed, the normal path returned 66 rows and the compiled query returned 100.

So the contract the docs state is that a compiled spec's construction must depend only on its declared arguments and on fixed configuration. The library enforces what it can see:

- arguments read in the constructor
- captured values and static members in the query
- factory captures
- `Items` and `PostProcessingAction`
- determinism across repeated runs

A branch on outside state stays a documented rule.

Other limits for the docs page:

- **EF's precompiled queries and NativeAOT can't include compiled specs.** They build their queries at runtime, and that's true of every OpenSpecification query today.
- **On EF Core 8 and 9 under C# 14, `Contains` on an array binds to the span overload,** which those versions can fail to translate. `List<T>` avoids it. EF Core 10 handles both.
- **Turn on context pooling first.** In Microsoft's benchmarks it saves more than compiling does.
- **Opting a spec in changes its constructor's signature,** which breaks binary compatibility for packages that ship specs.

## Evidence

The spike in [spike/](spike/) is a throwaway engine on net10.0, EF Core 10.0.14 and SQLite in memory. It covers `QueryArg<T>`, variants, one to three arguments, list, stream, first and count, includes, one projection, the per-model cache and the refusals. It leaves out parameterized `Search`, `Skip` and `Take`, paged, single and synchronous results, and generated overloads.

### Correctness: 19 checks, all passing

Each case runs compiled and on the normal path, and the rows match:

- the lookup by id with orders, across alternating ids including a missing one
- the search across all 4 shapes, alternated so a stale value would show, including an empty prefix
- counts
- a projection with id arrays of length 3, 0 and 1
- a null string parameter, which matches the null rows
- a fixed `Search` term
- the read-only variant, which changes tracking, with `ThenInclude` loading
- one handle on two context types, which builds exactly 2 delegates
- 200 parallel calls, each getting its own rows
- the 8 refusals above
- the outside-state limit

### Speed

BenchmarkDotNet 0.15.2, in-process, with 20 iterations and pooled contexts.

Lookup by id with orders. Its ratio to hand-written was steady across three runs:

| Method | Ratio to plain EF | Allocated |
|---|---:|---:|
| Plain EF | 1.00 | 20.32 KB |
| Spec today | 0.99 | 20.51 KB |
| Hand-written `EF.CompileAsyncQuery` | 0.73 | 11.88 KB |
| **Compiled spec** | **0.73** | **11.88 KB** |

Search with variants, about 14 rows. The allocations were steady, but the timings were too noisy on this machine after the last fix:

| Method | Allocated |
|---|---:|
| Hand-written, 4 delegates chosen by `if` | 16.49 KB |
| Hand-written behind a `Task<List<T>>` method | 16.56 KB |
| **Compiled spec** | **16.59 KB** |

Before EF stopped receiving shape-only arguments, the search's compiled spec was 4.7% slower and allocated 680 bytes more than hand-written. Now it allocates 100 bytes more, and returning `Task<List<T>>` accounts for 70 of them. Its timing needs a quieter machine.

These numbers come from SQLite in memory, so the percentage savings overstate what SQL Server will show. Microsoft measured 16% for a one-row query on SQL Server. The final numbers come from the home machine on SQL Server.

### Normal-path cost of opting in

A spec that takes `QueryArg` and still runs uncompiled somewhere pays for EF evaluating `Convert(closure.arg)`:

| Approach | Spec code | Lookup time | Lookup allocation |
|---|---|---:|---:|
| Spec today, `int` arguments | `c.Id == id` | 0.99 | 20.51 KB |
| A. Leave the conversion | `c.Id == id` | 1.10 | 22.65 KB |
| B. Write `.Value` | `c.Id == id.Value` | 1.05 | 20.78 KB |
| C. The builder strips the conversion when a lambda is added | `c.Id == id` | 1.02 | 21.10 KB |

Option C means the core builder scans every lambda of every spec. Building the search spec, with no database:

| Spec | Time | Allocated |
|---|---:|---:|
| Today, `int` arguments | 1.455 µs | 2.42 KB |
| Today, with the scan on every lambda | 1.436 µs | 2.45 KB |
| `QueryArg`, conversion left in | 1.876 µs | 2.77 KB |
| `QueryArg`, stripped | 2.597 µs | 3.49 KB |

The scan costs specs that don't use `QueryArg` no measurable time and about 30 bytes. A spec that opts in pays about 0.7 µs to strip when it's built, and gets back more than that when EF runs the query: 1.02 of plain EF against 1.10 with the conversion left in. **This revision recommends option C.** The spike's `QueryArgs.StripIfNeeded` stands in for what the core builder would do inside `Where`, `Search` and the other methods that take lambdas.

## Reviewer findings and where they stand

| Finding | From | Status |
|---|---|---|
| Outside state that changes construction can't be detected | Sol 1, Muse 1 | Contract narrowed and documented. Factories run eagerly at `Create`. Factory captures, `Items` and nondeterminism refused. The limit is shown in the spike |
| Custom evaluators can't promise normal-path results | Sol 2 | Default evaluator only in version one |
| Markers need registration identity, a concrete default and throwing observers | Sol 3, Muse 3 | Done in the spike |
| `Search`, `Skip` and `Take` need new storage | Sol 4, Muse 5 | Accepted, planned above, not in the spike |
| Behavior of each result type is undefined | Sol 5, Muse 5 | Specified above. `PostProcessingAction` refused |
| The hot path costs more than one delegate call | Sol 6 | Measured: parity on the lookup, 100 bytes on the search |
| Enum domains | Sol 7, Muse 2 | Aliases removed, `[Flags]` refused, unknown values throw |
| Variants have no identity | Muse 2 | Solved by `QueryArg` variants |
| Computed expressions on an argument move from client to SQL | Muse 3 | Arithmetic refused. Method calls on an argument, such as `ToUpper`, still pass and still need a decision |
| The generic handle can't name EF's delegate type | Muse 4 | Solved by handle types that name only the result and the arguments. Compiled in the spike |
| Model handling, and errors surfacing late | Muse 6 | Per-model cache, eager exploration, `Warm(DbContext)` |
| The argument against Muse's per-call model is only partly proved | Muse 7 | Accepted. Its per-call model stays a possible later tier |
| `QueryArg<string?>` is needed for an optional string | Muse API | Accepted. Passing it to a method that takes `string` needs `.Value!`. The parameterized `Search` overload covers the common case |

## Open questions

1. Whether method calls on an argument inside a query, such as `name.Value.ToUpper()`, should be refused in version one, as Muse suggested.
2. The search's timing against hand-written, on a quiet machine with SQL Server.
3. EF Core 8 and 9. The spike ran on 10 only. EF Core 9 with unused compiled parameters is no longer a question, since EF now receives only used arguments.
4. The name `QueryArg<T>`. Muse found `Param<T>` too short.

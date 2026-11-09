# Explicit compiled specification templates

I recommend an opt-in API in OpenSpecification.EntityFrameworkCore. A compiled template is a separate object with a fixed query shape and typed execution parameters. Existing specifications, interfaces, repositories, and extension methods retain their behavior. EF6 is outside this proposal.

The first release favors a small, enforceable contract over transparent compilation of arbitrary constructor-based specifications. In particular, it does not infer parameters from a sample specification or cache delegates by specification type.

## Public API

Add sealed `QueryParameter<T>` and `CompiledSpecification<TContext,T,T1>` types, plus a `CompiledSpecification` static factory. Add corresponding zero-parameter and two-parameter overloads and projection variants with an explicit TResult. Cap the initial API at two scalar parameters; reject requests beyond that rather than introducing object arrays or DynamicInvoke.

```csharp
sealed class CustomersByRegion : Specification<Customer>
{
    public CustomersByRegion(QueryParameter<int> region)
    {
        Query.Where(c => c.RegionId == region.Value);
        Query.OrderBy(c => c.Id);
        Query.AsNoTracking();
    }
}

static readonly CompiledSpecification<AppDbContext, Customer, int> ByRegion =
    CompiledSpecification.Create<AppDbContext, Customer, int>(
        region => new CustomersByRegion(region));

var customers = await ByRegion.ToListAsync(db, 42, cancellationToken);
await foreach (var customer in ByRegion.AsAsyncEnumerable(db, 43)
    .WithCancellation(cancellationToken))
{
    // Consume while db remains alive.
}
```

`Create` accepts `Func<QueryParameter<T1>, ISpecification<T>>`. Projection factories accept `ISpecification<T,TResult>`. Execution accepts a concrete context, not an arbitrary IQueryable. List and stream are the only initial operations. No repository interface changes are necessary: applications can call the handle from their own repository methods. A context-factory repository must create and retain its context until materialization completes; returning a stream after disposing the context is invalid.

`QueryParameter<T>.Value` is an expression marker. Its getter always throws a descriptive exception. It has no implicit conversion. Thus constructor branches, Search term strings, Skip and Take cannot accidentally consume placeholder values. Ordinary specifications can coexist with a separate constructor for compiled templates.

## Parameter construction

1. Create unique marker objects and invoke the factory exactly once at registration. The factory establishes a fixed shape. Attempting to branch on a marker throws immediately.
2. Read and snapshot the specification's metadata and expression trees. Reject PostProcessingAction and nonempty Items. Do not retain the mutable specification or its factory.
3. Walk all expressions before invoking EF. Recognize only an exact Value member access whose receiver is a closure-field chain ending in one of this registration's marker instances. Read fields only, never execute arbitrary getters. Replace each occurrence with the corresponding ParameterExpression of type T1 or T2. Repeated occurrences share the same parameter.
4. Refuse every remaining captured object, closure value, static field/property access, foreign marker, invocation expression, or unknown expression node. Permit scalar literals as fixed constants. Permit entity-rooted members and a deliberately documented whitelist of translatable methods. This conservative visitor must cover selectors, filters, ordering, and includes, not just Where.
5. Construct the IQueryable expression using a context parameter and `context.Set<T>()`. Apply the default evaluator pipeline at registration using an expression-recording IQueryable, then replace its root with that context expression. This recording provider must never execute a query. Validate again after evaluator expansion. Never leave a call to GetQuery, WithSpecification, the factory, or a specification object inside the EF lambda.
6. Construct a strongly typed `Expression<Func<TContext,T1,IQueryable<T>>>` and call the matching EF.CompileAsyncQuery overload, producing `Func<TContext,T1,IAsyncEnumerable<T>>`. Projection uses TResult. No runtime expression reconstruction, argument boxing array, or DynamicInvoke is required.
7. At execution, pass the current scalar values directly to the delegate. Materialize with async enumeration and cancellation, disposing the enumerator even on cancellation or failure.

Initially allow int, long, bool, string, Guid, decimal, double, float, DateTime and DateTimeOffset, plus nullable value variants. Reject enums, collections, entities, tuples and arbitrary value objects until provider tests establish their behavior. A scalar type passing this gate does not guarantee that every operation on it translates.

## Support boundary and diagnostics

Support fixed Where, ordering, scalar Select, SelectMany, fixed Skip/Take, fixed includes, tracking choices, IgnoreQueryFilters, IgnoreAutoIncludes, split queries and fixed tags through the default evaluator. Each feature remains subject to EF translation and the conservative expression visitor. Filtered includes can use marker parameters only where the visitor recognizes them. Simultaneous selectors retain the existing selector validation errors.

Search with a fixed term works through SearchEvaluator. Parameterized Search, Skip and Take are unsupported in version one because their builder inputs are already concrete strings and integers. Supporting them later requires new expression-valued template builders; reading marker.Value at construction must never be allowed. Conditional builder calls can depend only on fixed registration-time configuration. Document that each registration defines one shape.

Reject custom evaluators, arbitrary source composition, postprocessing, cache-enabled specifications, unknown Items, and captures even when their current value happens to be scalar. CacheKey is not a compiled-query identity. Existing result-cache semantics must not be implied by this API. Count, Any, First, Single, paged results and synchronous execution are deferred; users retain existing APIs for those operations.

Throw CompiledSpecificationNotSupportedException at registration with the feature and expression path, such as `Where[0].Right: captured field minimum; use QueryParameter<int>`. EF translation can fail on first enumeration because EF compilation is lazy. Preserve the original EF exception and identify the template in an outer exception without printing parameter values. Do not silently fall back to ordinary execution.

## Preventing silent wrong results

A type-only cache would reuse the wrong conditional filters or ordering. This design has no global shape cache: each registration owns its delegates. Two registrations of the same specification class remain separate.

Captured request values could become frozen constants. Reject all captures except exact marker receivers, including captures in projections and includes. Literal constants intentionally remain fixed. Static clocks and mutable global state must not enter the template. Context-dependent model filters need separate integration tests; never rewrite their context references into registration-time constants.

A delegate can otherwise be reused against the wrong model. Store delegates in a ConditionalWeakTable keyed by the actual context.Model reference, with a Lazy compiled delegate per entry. Use the same concrete context type and provider in each entry. Build each delegate independently for each model. Do not use a model hash or context type alone. Concurrent callers may share delegates but must use different contexts.

Mutation after registration could change apparent intent. Snapshot once and retain no specification reference. Mutation concurrent with registration is unsupported and documented. Factory side effects happen once; subsequent executions never rerun it.

Unknown metadata could be ignored by a closed evaluator pipeline. Accept only library Specification-derived instances, reject Items, and explicitly document that custom subclass state has no compiled semantics. Maintain a feature audit whenever specification metadata changes. Fixed tags and string include paths must participate in the snapshot.

Postprocessing and terminal semantics could diverge from repository behavior. Reject postprocessing, expose only list and stream, and document that these handles are separate operations. Tracking fixup, nondeterministic ordering, transaction isolation and pooled-context tenant initialization retain EF's normal semantics; compiled queries do not repair them.

## Implementation and verification plan

Put marker types, immutable snapshots, expression validation, query construction and handle types under a new CompiledQueries directory in the EF Core project. Keep core and EF6 unchanged. Use the existing default evaluator order as the source of truth rather than duplicating it. The recording IQueryable is an internal construction tool, never a public execution result.

First prove the expression-building path with a small SQL Server consumer on EF 8, 9 and 10. For each target, use a unique database and drop it in finally. Compare ordinary and compiled results for alternating values, nulls, empty strings and repeated values. Exercise different contexts concurrently, distinct models on the same context type, and tenant filters across reused context instances.

Then add regression coverage for every accepted feature, combinations of projection with pagination, grouped Search, filtered includes, tracking and split queries. Verify SQL parameters with an interceptor, not only returned rows. Negative tests must cover closure captures, marker use in constructor branches, foreign markers, unsupported scalar types, postprocessing, custom evaluators and mutation after registration. Cancellation tests must prove enumerator disposal and that factory-owned contexts remain alive through materialization. Test registration races and independent registrations of the same specification type.

Run repository tests using CONTRIBUTING.md and the shared SQL lock. Benchmark registration cost separately from steady-state execution against existing SpecificationEvaluator queries and handwritten EF compiled queries. Measure allocations and latency with identical data and context lifetimes. Do not promise a speedup before measuring.

Add docs/usage/compiled-queries.md explaining the marker constructor, fixed shapes, lifetime, supported types and feature limits, first-use translation errors, model partitioning and ordinary-query escape path. Include a migration example from a captured constructor argument to a marker. Add an additive feature entry to CHANGELOG.md when implemented.

## Evidence and uncertainty

I inspected ISpecification, SpecificationEvaluator, SearchEvaluator, PaginationEvaluator, DbSetExtensions and repository query/materialization paths. The evaluator applies ordinary features before projection. Pagination evaluates integer values while constructing the query, so symbolic pagination cannot pass through that builder unchanged. SearchExpressionInfo stores a concrete string term. DbSet list materialization applies postprocessing outside EF, which explains the explicit refusal here.

No prototype, build, test or benchmark was run for this design. The recording-provider approach, marker rewriting, compiled delegate construction, model partitioning and all proposed feature support remain implementation hypotheses requiring the tests above. This proposal is buildable guidance, not a claim that the API has been verified.

Microsoft documents scalar parameter restrictions and the single-model restriction, and shows the strongly typed asynchronous delegate pattern: [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries). That documentation supports the architecture; it does not prove this adapter's correctness.

## Alternatives

Automatic compilation of existing constructor specifications is attractive but cannot reliably distinguish mutable captures, structural values, literal constants and execution parameters. Rebuilding and structurally comparing every query can reduce that risk but consumes the overhead this feature aims to remove. A sample instance plus a type-keyed cache is unacceptable.

An expression such as `(db, id) => db.Set<Customer>().WithSpecification(new ById(id))` leaves specification construction inside EF's expression tree. Without explicit expansion it is not a translated query. An expansion framework could support it, but adds a substantially larger partial evaluator and constructor interpretation problem.

A plain EF expression API would be simpler and useful, but users can already call EF.CompileAsyncQuery directly. Typed marker templates preserve the existing specification builder and its evaluator ordering while making dynamic values explicit. The cost is a separate constructor or specification for compiled use and conservative first-release support.

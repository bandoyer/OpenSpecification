# EF Core compiled query support for OpenSpecification — design

Status: design only. No library code in this document has been merged. It proposes an
additive feature for `OpenSpecification.EntityFrameworkCore` only. The core package and
the EF6 package do not change.

## 1. Summary

A specification captures its arguments in expression trees, for example
`Query.Where(x => x.Age > age)` where `age` is a constructor argument. EF Core can turn
each such query into SQL in two ways. The normal way parameterises the captured values
every time the query runs. A compiled query, made with `EF.CompileAsyncQuery`, translates
the query once and returns a strongly typed delegate that the caller keeps and reuses
with new parameter values.

This design adds compiled query support without changing any existing type or behaviour:

1. It rewrites a specification's expression trees. Every captured value that does not
   depend on the entity becomes a typed parameter of a new expression whose root is a
   `DbContext` parameter. Every other part of the tree stays as it is.
2. It hands that expression to `EF.CompileAsyncQuery` through reflection, because the
   parameter types and count are only known at runtime. The delegate EF Core returns is
   strongly typed. No parameter is boxed as `object`, because EF Core cannot assign a
   type mapping to an `object` parameter (proved in section 10).
3. It caches the delegate under a key that describes the exact shape of the query, so two
   specifications that build different queries never share a delegate.
4. It refuses, with a new exception, any specification it cannot compile safely. It never
   falls back to the normal path silently.

The user-facing surface is three things, all new: a `CompiledSpecification` handle that a
user creates once for a hot query, terminal extension methods on `DbContext` that compile
on first use and reuse after that, and matching methods on the two EF Core repository
base classes. No interface gains a member, so a class that implements
`IReadRepositoryBase<T>` or `IRepositoryBase<T>` today still builds.

Compiled queries are terminal. EF Core returns an `IAsyncEnumerable<TResult>` or a
`Task<TResult>` from a compiled delegate, not an `IQueryable`. There is therefore no
compiled form of `WithSpecification`, and nothing in this design returns an `IQueryable`
that pretends to be one. Chaining two specifications, or adding a further `Where` after
the call, stays on the existing non-compiled path.

## 2. What exists today, and the facts the design rests on

Code references are to this checkout at version 10.1.0.

- `SpecificationEvaluator.GetQuery` in
  [SpecificationEvaluator.cs](../src/OpenSpecification.EntityFrameworkCore/Evaluators/SpecificationEvaluator.cs:41)
  applies a fixed list of evaluators, in order, to a caller-supplied `IQueryable<T>`, and
  then applies `Select` or `SelectMany`. The evaluator list is built in the constructor at
  line 17: Where, Search, IncludeString, Include, Order, Pagination, then the flag
  evaluators and QueryTag. `evaluateCriteriaOnly: true` keeps only evaluators whose
  `IsCriteriaEvaluator` is true. This design mirrors that order and that split.
- Where, Search and Order keep an `Expression<...>` and also compile it to a delegate for
  in-memory use. See
  [WhereExpressionInfo.cs](../src/OpenSpecification/Expressions/WhereExpressionInfo.cs:25),
  [SearchExpressionInfo.cs](../src/OpenSpecification/Expressions/SearchExpressionInfo.cs:32)
  and
  [OrderExpressionInfo.cs](../src/OpenSpecification/Expressions/OrderExpressionInfo.cs:28).
  A compiled query needs the expression form only.
- A specification is state on `Specification<T>`: where/search/order/include expression
  lists, include strings, query tags, `Skip` and `Take` as `int` with `-1` for unset, flag
  booleans, `CacheKey`, `Items`, and on `Specification<T, TResult>` a `Selector`,
  `SelectorMany` and `PostProcessingAction`. See
  [Specification.cs](../src/OpenSpecification/Specification.cs:92)
  and
  [ISpecification.cs](../src/OpenSpecification/ISpecification.cs:37).
- `RepositoryBase<T>` holds a `DbContext` and an `ISpecificationEvaluator`, and applies a
  specification with `SpecificationEvaluator.GetQuery(DbContext.Set<T>()...)`. See
  [RepositoryBaseOfT.cs](../src/OpenSpecification.EntityFrameworkCore/RepositoryBaseOfT.cs:251).
  `ContextFactoryRepositoryBaseOfT<TEntity, TContext>` does the same per call with a
  context from an `IDbContextFactory<TContext>`. A repository can be built with a custom
  evaluator, which this design must respect (section 6).
- `DbSetExtensions` offers `WithSpecification`, which returns an `IQueryable`, and terminal
  methods such as `ToListAsync(spec)`. See
  [DbSetExtensions.cs](../src/OpenSpecification.EntityFrameworkCore/Extensions/DbSetExtensions.cs:249).
- `SearchExtension` builds `EF.Functions.Like` calls and holds the search term as a member
  access on a small record so EF Core parameterises it. See
  [SearchExtension.cs](../src/OpenSpecification.EntityFrameworkCore/Evaluators/SearchExtension.cs:19).
  This design builds the same call with the term as a compiled parameter instead.
- The EF Core package targets net8.0, net9.0 and net10.0 against EF Core 8.0.19, 9.0.8 and
  10.0.14. The core package also targets netstandard2.0. Nothing compiled-query related can
  go in the core package, because `EF.CompileAsyncQuery` lives in EF Core and the core
  package must keep building for netstandard2.0 with no EF dependency.

### 2.1 EF Core compiled query facts used here

These come from the EF Core API in the referenced packages and from the probes in
section 10.

- `EF.CompileAsyncQuery` takes an `Expression<Func<TContext, ...>>`. For a query it takes
  `Expression<Func<TContext, IQueryable<TResult>>>` plus zero or more typed parameters
  and returns `Func<TContext, ..., IAsyncEnumerable<TResult>>`. For a single result it
  takes an expression returning `TResult`, with an overload that also takes a
  `CancellationToken`, and returns `Func<TContext, ..., Task<TResult>>`.
- The overloads accept at most 15 query parameters after the context. Past that there is
  no overload to call. The code must find the overload at runtime rather than hard-code
  the count, because the three EF Core versions in use need not agree.
- Every parameter is typed in the delegate. An `object` parameter fails when the query
  first runs, with "does not have a type mapping assigned". A single params object or a
  tuple has the same problem. This rules out the easy untyped design.
- A compiled delegate is bound to one context type. The model EF Core builds for that
  type is part of what the delegate assumes.
- The normal evaluator path cannot be reused as the input to `EF.CompileAsyncQuery`.
  `SpecificationEvaluator.GetQuery` returns an `IQueryable` whose expression is rooted at
  an EF Core entity query root, not at a `DbContext` parameter. A compiled query needs an
  expression built from scratch with `ctx.Set<T>()` at its root. Section 10 proves the
  root difference. This is the main implementation cost: a second, expression-level
  builder that mirrors the evaluators.

## 3. Public API

All new types are in the `OpenSpecification.EntityFrameworkCore` namespace. Nothing is
added to `OpenSpecification` (the core package), to any existing interface, or to the EF6
package.

### 3.1 The handle: `CompiledSpecification`

The handle is the recommended form for a known hot query. The user creates it once,
usually in a `static readonly` field, from a template specification instance. The
template fixes the query shape and the parameter types. Its argument values do not
matter, but its types do, so a template is normally built with dummy values of the right
types, for example `new CustomersByAgeSpec(0)`.

```csharp
public static class CompiledSpecification
{
    public static CompiledSpecification<TContext, T> Create<TContext, T>(
        ISpecification<T> template)
        where TContext : DbContext
        where T : class;

    public static CompiledSpecification<TContext, T, TResult> Create<TContext, T, TResult>(
        ISpecification<T, TResult> template)
        where TContext : DbContext
        where T : class;
}

public sealed class CompiledSpecification<TContext, T>
    where TContext : DbContext
    where T : class
{
    public IReadOnlyList<Type> ParameterTypes { get; }
    public Task<List<T>> ToListAsync(TContext context, ISpecification<T> specification, CancellationToken cancellationToken = default);
    public Task<T?> FirstOrDefaultAsync(TContext context, ISpecification<T> specification, CancellationToken cancellationToken = default);
    public Task<T?> SingleOrDefaultAsync(TContext context, ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default);
    public Task<int> CountAsync(TContext context, ISpecification<T> specification, CancellationToken cancellationToken = default);
    public Task<bool> AnyAsync(TContext context, ISpecification<T> specification, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<T> AsAsyncEnumerable(TContext context, ISpecification<T> specification);
}

public sealed class CompiledSpecification<TContext, T, TResult>
    where TContext : DbContext
    where T : class
{
    public IReadOnlyList<Type> ParameterTypes { get; }
    public Task<List<TResult>> ToListAsync(TContext context, ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
    public Task<TResult?> FirstOrDefaultAsync(TContext context, ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
    public Task<TResult?> SingleOrDefaultAsync(TContext context, ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<TResult> AsAsyncEnumerable(TContext context, ISpecification<T, TResult> specification);
}
```

Notes on this shape:

- Generic order is `TContext` first, as in `EF.CompileAsyncQuery`, then the entity, then
  the result. This differs from `ContextFactoryRepositoryBaseOfT<TEntity, TContext>`,
  which puts the entity first. The compiled handle is read alongside EF Core's own API,
  so it follows EF Core.
- `Create` does the full static work: it builds the rewritten expression, computes the
  shape key (section 5), checks every refusal rule that does not need a context
  (section 6), and records the parameter types. It does not need a context instance.
  Whether EF Core translates the query at delegate creation or at first invocation is an
  EF Core detail (section 11 lists it as assumed); either way, the first call for each
  terminal operation pays any remaining translation cost, and later calls do not.
- Each terminal operation has its own delegate, built on first use of that operation.
  `ToListAsync` and `AsAsyncEnumerable` share one enumeration delegate. `CountAsync` and
  `AnyAsync` use a criteria-only delegate, as the existing `CountAsync` does.
- `ParameterTypes` is there for tests, logs and startup checks. It lets a user assert at
  startup that a specification they expect to have two parameters really has two.
- The handle checks every specification instance it is given. If the instance builds a
  different shape than the template, it throws `CompiledSpecificationNotSupportedException`
  rather than run the template's query with the instance's values (section 7).
- A handle is thread-safe. The delegates EF Core returns can be called concurrently, and
  the handle keeps no mutable per-call state. Specification instances are read once, at
  the start of a call, into a value array; a caller who mutates a specification while a
  call using it is in flight gets the same undefined behaviour as on the normal path.

### 3.2 Convenience: `DbContext` extension methods

For a query that is hot but not worth a field, new extension methods on `DbContext`
compile on first use and cache the delegate in a process-wide cache keyed by shape.
They live in a new static class, `DbContextCompiledExtensions`, in a new file. They are
not added to `DbSetExtensions`, because a compiled query needs the context, and a
`DbSet<T>` does not expose its context through a public API this library should depend on.

```csharp
public static class DbContextCompiledExtensions
{
    public static Task<List<T>> ToListCompiledAsync<T>(this DbContext context, ISpecification<T> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<List<TResult>> ToListCompiledAsync<T, TResult>(this DbContext context, ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<T?> FirstOrDefaultCompiledAsync<T>(this DbContext context, ISpecification<T> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<TResult?> FirstOrDefaultCompiledAsync<T, TResult>(this DbContext context, ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<T?> SingleOrDefaultCompiledAsync<T>(this DbContext context, ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<TResult?> SingleOrDefaultCompiledAsync<T, TResult>(this DbContext context, ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<int> CountCompiledAsync<T>(this DbContext context, ISpecification<T> specification, CancellationToken cancellationToken = default) where T : class;
    public static Task<bool> AnyCompiledAsync<T>(this DbContext context, ISpecification<T> specification, CancellationToken cancellationToken = default) where T : class;
    public static IAsyncEnumerable<T> AsCompiledAsyncEnumerable<T>(this DbContext context, ISpecification<T> specification) where T : class;
    public static IAsyncEnumerable<TResult> AsCompiledAsyncEnumerable<T, TResult>(this DbContext context, ISpecification<T, TResult> specification) where T : class;
}
```

The context's runtime type is the `TContext` for that call. A user who calls these on a
base-typed variable still gets a delegate for the actual context type, because the
methods read `context.GetType()`.

### 3.3 Repositories

`RepositoryBase<T>` and `ContextFactoryRepositoryBaseOfT<TEntity, TContext>` each gain
virtual methods with the same names minus the `DbContext` receiver, mirroring their
existing methods:

```csharp
// On RepositoryBase<T>, and with TEntity on ContextFactoryRepositoryBaseOfT<TEntity, TContext>:
public virtual Task<List<T>> ListCompiledAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
public virtual Task<List<TResult>> ListCompiledAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
public virtual Task<T?> FirstOrDefaultCompiledAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
public virtual Task<TResult?> FirstOrDefaultCompiledAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
public virtual Task<T?> SingleOrDefaultCompiledAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default);
public virtual Task<TResult?> SingleOrDefaultCompiledAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default);
public virtual Task<int> CountCompiledAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
public virtual Task<bool> AnyCompiledAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
public virtual IAsyncEnumerable<T> AsCompiledAsyncEnumerable(ISpecification<T> specification);
public virtual IAsyncEnumerable<TResult> AsCompiledAsyncEnumerable<TResult>(ISpecification<T, TResult> specification);
```

These are new methods on concrete classes. `IReadRepositoryBase<T>` and
`IRepositoryBase<T>` do not change, so a hand-written implementation of either interface
still builds and loads. A repository built with a custom `ISpecificationEvaluator`
cannot honour that evaluator on the compiled path, because the compiled builder mirrors
the default evaluators only. Its compiled methods throw
`CompiledSpecificationNotSupportedException` and name the custom evaluator as the reason.
`RepositoryBase<T>` knows its evaluator from its constructor, so the check is one
reference comparison against `SpecificationEvaluator.Default`.
`ContextFactoryRepositoryBaseOfT` has the same check. Section 6 covers this.

`ContextFactoryRepositoryBaseOfT` creates a context per call, as its existing methods
do, and for `AsCompiledAsyncEnumerable` it creates one per enumeration and disposes it
when enumeration ends, matching its current fix for that method.

### 3.4 The exception

One new public exception, in the EF Core package:

```csharp
public sealed class CompiledSpecificationNotSupportedException : NotSupportedException
{
    public CompiledSpecificationNotSupportedException(Type specificationType, string feature, string reason);
    public Type SpecificationType { get; }
    public string Feature { get; }
}
```

Every refusal in section 6 throws this, with a message that names the specification type,
the feature, and what to change. There is no warning-and-continue path and no silent
fallback to `SpecificationEvaluator`.

### 3.5 Example

A specification as users write it today. Nothing about it changes.

```csharp
public class CustomersByAgeSpec : Specification<Customer>
{
    public CustomersByAgeSpec(int minimumAge, string? namePrefix)
    {
        Query.Where(x => x.Age > minimumAge)
             .Where(x => namePrefix == null || x.Name!.StartsWith(namePrefix), namePrefix is not null)
             .Include(x => x.Orders)
             .OrderBy(x => x.Name)
             .AsNoTracking();
    }
}
```

Normal path today, unchanged:

```csharp
List<Customer> customers = await repository.ListAsync(new CustomersByAgeSpec(21, "A"));
```

Compiled, through the repository. The first call for this shape compiles. Later calls
with other values reuse the delegate:

```csharp
List<Customer> customers = await repository.ListCompiledAsync(new CustomersByAgeSpec(21, "A"));
List<Customer> older = await repository.ListCompiledAsync(new CustomersByAgeSpec(65, null));
```

Compiled, through a context:

```csharp
List<Customer> customers = await dbContext.ToListCompiledAsync(new CustomersByAgeSpec(21, "A"));
int count = await dbContext.CountCompiledAsync(new CustomersByAgeSpec(21, "A"));

await foreach (Customer customer in dbContext.AsCompiledAsyncEnumerable(new CustomersByAgeSpec(21, "A")))
{
    // ...
}
```

Compiled, as a handle kept by the caller. This is the form to use on a known hot path,
because the shape is checked once at startup instead of on every call:

```csharp
public class CustomerQueries(AppDbContext db)
{
    private static readonly CompiledSpecification<AppDbContext, Customer> ByAge =
        CompiledSpecification.Create<AppDbContext, Customer>(new CustomersByAgeSpec(0, null));

    public Task<List<Customer>> OlderThan(int age, string? prefix) =>
        ByAge.ToListAsync(db, new CustomersByAgeSpec(age, prefix));
}
```

If `CustomersByAgeSpec` is later edited so an instance builds a different shape than the
template — for example the second `Where` becomes unconditional — the handle throws on
the first call whose shape differs. It does not run the old query with new values.

## 4. How a specification's values become parameters, step by step

This is the centre of the design. It runs in the EF Core package, in a new internal
engine. The engine has four parts: a canonical builder, a captured-value rewriter, a
shape key, and a delegate factory. The handle and the convenience methods are thin
wrappers over it.

### 4.1 Build a canonical expression rooted at the context

Given a specification, a terminal operation (list, first, single, count, any), a context
type and a criteria-only flag, the builder produces one expression tree:

1. Create `ctx` as a `ParameterExpression` of the context type.
2. Create the root as a call to `DbContext.Set<T>()` on `ctx`. This is the only
   expression that mentions the context. It is what makes the tree a valid input to
   `EF.CompileAsyncQuery`.
3. Apply the specification in the same order as `SpecificationEvaluator`:
   Where, Search, IncludeString, Include, Order, Pagination, the flag evaluators
   (`AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`,
   `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `AsSplitQuery`), then QueryTag. Each step
   wraps the current expression in the same `Queryable` or
   `EntityFrameworkQueryableExtensions` call the corresponding evaluator makes. For a
   criteria-only build (Count and Any), stop after the steps whose evaluator has
   `IsCriteriaEvaluator` true, which also drops Include, Order and Pagination, as the
   existing count does.
4. For a projected specification, wrap with `Queryable.Select` or
   `Queryable.SelectMany` using the rewritten `Selector` or `SelectorMany`. Check the
   existing rules first: no selector throws `SelectorNotFoundException`, both selectors
   throw `ConcurrentSelectorsException`, exactly as
   [SpecificationEvaluator.cs](../src/OpenSpecification.EntityFrameworkCore/Evaluators/SpecificationEvaluator.cs:44)
   does today.
5. Add the terminal. List and stream leave the expression as an `IQueryable<TResult>`.
   First and Single append a parameterless `FirstOrDefault` or `SingleOrDefault` call.
   Count appends `Count`. Any appends `Any`. `PostProcessingAction` is never part of the
   tree (section 6).

Every lambda taken from the specification (each `Where` filter, each Search selector,
each Order key selector, the `Select` or `SelectMany` selector) passes through the
rewriter in section 4.2 before it is placed in the tree. The Search pattern is not in a
lambda; `SearchExpressionInfo.SearchTerm` is a plain string on the specification, so
the builder adds it directly as a string parameter and builds the `EF.Functions.Like`
call around the rewritten selector, following
[SearchExtension.cs](../src/OpenSpecification.EntityFrameworkCore/Evaluators/SearchExtension.cs:40).

`Skip` and `Take` come from `Specification<T>.Skip` and `.Take`, not from a lambda. When
`Skip > 0` the builder adds a `Skip` call with a new `int` parameter. When `Take >= 0`
it adds a `Take` call with a new `int` parameter. Section 10 proves EF Core accepts
`Skip` and `Take` as compiled parameters. A specification with neither adds no parameter
and no call, as `PaginationEvaluator` does today.

Include lambdas are the exception. An include is checked, not rewritten (section 6).

### 4.2 Rewrite captured values into parameters

When C# compiles `x => x.Age > age`, the tree is
`x => x.Age > value(Closure).age`: a member access on a constant closure object. When the
argument is a property of the specification itself, the root is a constant `this`. The
rewriter's job is to find every subexpression that holds a runtime value and turn it into
a parameter, while leaving the parts EF Core must translate.

The rewriter is an `ExpressionVisitor` run separately on each lambda. It works out, for
each subtree, whether the subtree mentions the lambda's entity parameter.

- If a subtree mentions the entity parameter, it is translatable structure. The rewriter
  recurses into its children and lifts only the child subtrees that do not mention the
  entity. Example: in `x => x.Age > age + 5`, the right side of `>` does not mention
  `x`, so the whole `age + 5` becomes one `int` parameter. In
  `x => x.Name.Contains(term)`, the argument `term` becomes one string parameter. In
  `x => x.Orders.Any(o => o.Total > minimum)`, the inner lambda gets the same treatment
  with both `x` and `o` counted as entity parameters.
- If a subtree does not mention the entity parameter, the rewriter decides whether it is
  a captured value or a literal:
  - It is a captured value if its tree contains a member access rooted at a constant
    (a closure field, or a field or property of the specification instance), or a method
    call or arithmetic on such a member, or a static member that reads current state
    such as `DateTime.Now`. The maximal such subtree becomes one parameter. Parameter
    type is the subtree's expression type, after any conversion at its root, so
    `(long)age` becomes a `long` parameter whose value is the converted value.
  - It is a literal if it is a plain constant or a combination of constants only
    (`18`, `"abc"`, `2 + 3`). Literals stay in the tree. They are part of the shape, so
    `x => x.Age > 18` and `x => x.Age > 21` are different shapes with different
    delegates. That costs a delegate per distinct literal. Specifications that hard-code
    a threshold in the lambda, instead of taking it as an argument, get no reuse for
    that threshold, which is correct and visible in the shape key during testing.
  - A member access on `EF.Functions` is neither. It is EF Core's marker and is left
    alone; only its arguments are visited.
- Two occurrences of the same closure member (the same field on the same closure type)
  become one parameter. Two occurrences of a computed subtree each become their own
  parameter, because a computed subtree might not be pure. `age` used twice is one
  parameter; `DateTime.Now` used twice is two, each read at call time.

Placeholders are numbered in the order the builder meets them, which is fixed: Where
expressions in specification order, then Search (by group, then specification order,
selector captures before the pattern), then Order, then Select or SelectMany, then Skip,
then Take. That order is the parameter order of the delegate. It never depends on
dictionary order or on reflection order over the specification's fields.

### 4.3 Read the values out of a specification instance

A template build records, for each placeholder, its position and its type. To run a
call, the engine runs the same rewriter over the given instance's lambdas in extract
mode. In extract mode the rewriter evaluates each maximal captured subtree instead of
replacing it. Member chains rooted at a constant are read by walking the member chain
with reflection from the constant value. Anything else (arithmetic, a method call on a
captured value, a static member) is evaluated by compiling that one small subtree to a
`Func<object?>` and invoking it. That compile happens per call and per computed
parameter. It is small next to a database round trip, and it is the price of supporting
`age + 5` or `term.ToUpper()` without asking users to change how they write
specifications. A future optimisation can cache the compiled extractors per shape;
section 12 lists it as a follow-up, not part of this design.

Extract mode also validates, before any delegate runs:

- The instance produced the same number of placeholders, in the same positions, as the
  template. If not, the shapes differ; throw.
- Each value's runtime type matches its placeholder type exactly, allowing `null` only
  where the placeholder type is a reference type or a nullable value type. A
  specification whose constructor stores an `int` in an `object` field produces an
  `object`-typed subtree, which section 6 already refused at build time, so this check
  is a second line of defence, not the only one.
- A collection value is not null (section 6).

The values are copied into an `object?[]` snapshot before the first `await`. The
specification is not read again after that point.

### 4.4 Build the strongly typed delegate

The builder now has a rewritten body and a list of placeholder types `P1..Pn`.

1. Build the delegate type. For a list or stream it is
   `Func<TContext, P1, ..., Pn, IQueryable<TResult>>` as an expression type, and EF Core
   returns `Func<TContext, P1, ..., Pn, IAsyncEnumerable<TResult>>`. For First, Single,
   Count and Any, the expression returns the scalar or entity result and the code asks
   for the overload that also takes a `CancellationToken`, so the returned delegate is
   `Func<TContext, P1, ..., Pn, CancellationToken, Task<TResult>>`. Reflecting over
   `typeof(EF)` finds the overload whose expression type matches; this is how the code
   stays correct across EF Core 8, 9 and 10 and how it discovers the 15-parameter limit
   instead of assuming it. If `n` exceeds the largest overload, throw
   `CompiledSpecificationNotSupportedException` naming the count.
2. Build the lambda with `Expression.Lambda(delegateType, body, ctx, p1..pn[, token])`
   and invoke the chosen `EF.CompileAsyncQuery` overload through
   `MethodInfo.MakeGenericMethod`. Section 10 proves a runtime-built lambda of exactly
   this form compiles and returns correct, distinct results for distinct parameter
   values.
3. Wrap the returned delegate in an invoker: a compiled
   `Func<DbContext, object?[], CancellationToken, ...>` that casts the context, unboxes
   each element of the value array to its placeholder type, and calls the EF Core
   delegate. The unboxing is inside a compiled expression, so the per-call cost is a
   cast per parameter, not `DynamicInvoke`. The invoker is built once per shape by
   closing a generic engine class over `TContext`, `T` and `TResult` with
   `MakeGenericType`, so the list path can materialise a real `List<T>` and apply a
   typed `PostProcessingAction` without further reflection.

The engine caches one invoker per (shape key, terminal, criteria-only flag). The handle
keeps its invokers in its own fields. The convenience methods keep theirs in a static
`ConcurrentDictionary` described in section 5.

### 4.5 Run the call

- List: invoke the enumeration delegate, collect with
  `await foreach ... WithCancellation(token)`, then apply `PostProcessingAction` if the
  specification has one, exactly as `RepositoryBase.ListAsync` does today.
- Stream: return the delegate's `IAsyncEnumerable<T>` with
  `WithCancellation` applied by the caller's enumeration, as the existing
  `AsAsyncEnumerable` does. `PostProcessingAction` is not applied, matching the existing
  stream behaviour, and the docs say so.
- First and Single: invoke the scalar delegate with the caller's token. EF Core's
  `SingleOrDefault` semantics give the same "more than one row" exception as today.
- Count and Any: invoke the criteria-only scalar delegate. Pagination, ordering and
  includes do not affect the result, as today.

## 5. The shape key: what decides that two calls share a delegate

Sharing a delegate between two different queries is the one failure that returns wrong
rows with no error. The shape key is the guard, so it is conservative: anything that
could change the SQL is in the key, even if that means two keys where one delegate
would have done.

The key is a string built by a canonical printer over the rewritten tree, plus a fixed
prefix. It contains:

- The terminal operation and the criteria-only flag.
- The assembly-qualified names of `TContext`, `T` and `TResult`, and the EF Core
  provider name taken from the context at convenience-call time (`Database.ProviderName`).
  A handle has no context at creation, so its key takes the provider from the first
  context it runs against and checks it, and the context type, on later calls
  (section 7).
- A canonical printing of the rewritten expression: node kinds, method identities
  including generic arguments, member identities (declaring type and name), entity
  parameter positions, and for each placeholder its index and CLR type.
- Every literal constant left in the tree, printed with invariant formatting, strings
  quoted and escaped. Two trees that differ only in a hard-coded literal get different
  keys.
- The specification's flag booleans (`AsNoTracking`, `AsTracking`,
  `AsNoTrackingWithIdentityResolution`, `AsSplitQuery`, `IgnoreQueryFilters`,
  `IgnoreAutoIncludes`), each as a name and a `0` or `1`.
- Each query tag string, in order.
- Each include string, in order, and for each lambda include the member chain it names
  and whether it is an `Include` or a `ThenInclude` with its previous property type.
- Each Search group number, in order.
- Whether `Skip` is present, whether `Take` is present. Their values are parameters, so
  the values are not in the key.
- For a projection, which of `Selector` and `SelectorMany` is present.

The dictionary key is the whole string, not a hash of it. A hash collision must never be
able to merge two shapes; using the string means the dictionary's own equality check is
the final check. The string is also printable, so a test failure or a log line can show
exactly which shape was compiled. `CompiledSpecification.ParameterTypes` and an internal
shape description exist so tests can assert on this without a database.

Two consequences follow, both intended:

- A specification with a conditional clause, such as
  `Where(predicate, condition)` or `Include(path, condition)` or a conditional
  `OrderBy`, builds a different shape for each combination of conditions that fires.
  Each combination gets its own delegate. That is correct, and it is how a user finds
  out that a specification with many independent conditions compiles many delegates:
  the cache grows by combination, not by value. The docs section (section 9) tells
  users to prefer conditions on values (an always-present `Where` whose predicate
  handles the empty case) over conditions on shape for hot queries.
- Two different specification classes that happen to build the same tree share a
  delegate through the convenience cache. That is safe because the key is the tree, not
  the class, and because values are extracted from the instance's own tree on every
  call. The handle does not rely on this; it checks each instance against its template.

## 6. What works, what is refused, and how the user finds out

"Refused" always means `CompiledSpecificationNotSupportedException` thrown before any
query runs: at `CompiledSpecification.Create` for rules that need no context, otherwise
at the first compiled call for that shape. The message names the specification type,
the feature, and the fix. There is no log-warning path. A user who does not opt into a
compiled method never sees this exception, because no existing method changes.

### 6.1 Supported

- Any number of `Where` clauses, including nested lambdas (`Any`, `All`), string and
  numeric methods EF Core translates, and captures of any supported scalar (below).
- `Search` with any number of groups. Selectors are rewritten like Where lambdas; each
  pattern is a string parameter. Section 10 proves a compiled `EF.Functions.Like` with a
  string parameter returns distinct correct results per pattern.
- `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` chains, in specification
  order, including the existing duplicate-chain rules.
- `Include` with a lambda that is a pure navigation chain (`x => x.Orders`,
  `ThenInclude(o => o.Lines)`), and `Include` with a string. Both appear in the shape
  key.
- `Skip` and `Take` as `int` parameters.
- `Select` and `SelectMany`, with captures in the projection lifted like anywhere else.
  Section 10 proves a captured string in a `Select` works as a parameter.
- `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `AsSplitQuery`,
  `IgnoreQueryFilters`, `IgnoreAutoIncludes`, and query tags. These are shape, not
  parameters.
- `PostProcessingAction` on list calls only. It runs in memory after materialisation, as
  today. It is the one part of a specification that is never compiled, and the docs say
  so.
- Conditional `Where`/`Search`/`Include`/`OrderBy` (the `condition` overloads). Each
  firing combination is a separate shape and a separate delegate.
- A custom class implementing `ISpecification<T>` directly, as long as it exposes its
  expressions through the interface and has an empty `Items`. The engine reads only the
  interface properties, in the evaluator's order.

Supported parameter types, checked against the placeholder's expression type:

- `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`,
  `double`, `decimal`, `char`, `string`, `Guid`, `DateTime`, `DateTimeOffset`,
  `TimeSpan`, and on runtimes where the type exists `DateOnly` and `TimeOnly`.
- Any enum type. The parameter is typed as the enum, not its underlying type, so EF Core
  applies the model's enum mapping. Section 10 proves an enum parameter filters
  correctly.
- The nullable form of any value type above.
- A collection of a supported scalar, only as `T[]` or `List<T>`, used in a `Contains`
  the way specifications use it for an `IN` list. Section 10 proves a `List<int>`
  parameter gives correct results for lengths 0, 1 and 3 through one delegate on SQL
  Server with EF Core 10. A null collection is refused at call time. Any other
  collection type (`HashSet<T>`, `IEnumerable<T>` as the captured type, a custom
  collection) is refused with a message to materialise to an array or a list in the
  specification constructor.

### 6.2 Refused

- More than 15 parameters after de-duplication. The message gives the count and says to
  reduce captures or split the specification. The limit is discovered from the EF Core
  overloads at runtime, so the message reports the limit of the EF Core in use.
- A captured value whose expression type is `object`. This is the common shape of a
  specification that stores its arguments in `object` fields or takes `object`
  arguments. EF Core cannot map it (section 10). The message says to type the field or
  argument as the real type.
- A captured entity, a captured `DbContext` or `DbSet`, a captured delegate, a captured
  `IQueryable`, or any captured type not in the supported list, including an anonymous
  type and a user class used as a value object in a comparison. EF Core itself rejects
  an entity parameter when the query runs (section 10); this design rejects it earlier
  with a clearer message.
- A captured `null` whose type cannot be determined from the tree (a bare `null`
  constant typed as `object`). A typed null is fine: `string? name = null` captured in
  `x => x.Name == name` is a string parameter whose value is null, and section 10 proves
  it matches the null row and not others.
- An `Include` lambda that is not a pure navigation chain. That covers a filtered
  include (`x => x.Orders.Where(o => o.Total > minimum)`) and any include that captures
  a value. Filtered includes are refused in this design even though EF Core supports
  them in a normal query, because the existing `IncludeEvaluator` delivers the lambda
  to EF Core by reflection and a compiled include needs its own treatment. This is the
  largest functional gap, and section 12 lists it as the first follow-up.
- A specification with a non-empty `Items` dictionary. `Items` is arbitrary state that a
  custom evaluator could read; the compiled builder cannot see that, so it refuses
  rather than guess.
- A repository built with a custom `ISpecificationEvaluator`, for its compiled methods.
  The compiled path honours the default evaluators only.
- A projected specification with no selector, or with both `Select` and `SelectMany`.
  These throw the existing `SelectorNotFoundException` and
  `ConcurrentSelectorsException`, not the new exception, so behaviour matches the normal
  path.
- `WithSpecification` composition. There is no compiled overload. A user who needs to
  compose specifications uses the existing method; a user who needs a compiled query
  uses a terminal compiled method on the whole specification.
- The EF6 package. It gains nothing. EF6 has no equivalent of `EF.CompileAsyncQuery`
  with this parameter model, and the brief makes EF6 optional.

### 6.3 How a user finds out

Three ways, in order of preference. First, the exception at startup or first call, with
the feature and the fix in the message. Second, `ParameterTypes` and a shape description
they can log or assert in a test, so a team can pin the compiled shapes they depend on.
Third, the docs page (section 9), which lists the supported parameter types and the
refusals as a checklist a user can compare a specification against before converting it.

## 7. Ways this design could return wrong results, and the guard for each

Listed worst first. "Guard" means a mechanism in the design, not a hope.

1. **Two different queries share one delegate.** A conditional specification is the
   realistic cause: the same class builds a filtered query for one call and an
   unfiltered query for another, and a cache keyed by class hands the second call the
   first call's query. Guard: the cache key is the canonical printing of the rewritten
   tree plus flags, tags, includes, groups and terminal (section 5), never the class
   alone. The handle separately compares each instance's key against its template's key
   and throws on any difference. Tests must include pairs of specifications that differ
   in exactly one feature and assert both the keys differ and the rows differ
   (section 8).
2. **Parameters are fed in the wrong order, or with the wrong types.** If values were
   read from the specification's fields by reflection, field order would decide. Guard:
   values are extracted by running the same rewriter over the instance's own expression
   trees, in the builder's fixed order, so position `i` in the value array is
   placeholder `i` by construction. The engine then checks the count and each runtime
   type against the template before invoking. A specification class whose lambda
   captures an `int` in one instance and a `long` in another (only possible through an
   `object` field, already refused, or a changed overload) fails the check and throws.
3. **A parameter is boxed as `object` and EF Core maps it loosely or not at all.**
   Guard: placeholders keep their expression types end to end, conversions included, and
   `object`-typed captures are refused at build time. Section 10 shows the `object` form
   failing in EF Core, which is why this is a refusal and not a fallback.
4. **Null means something different as a parameter.** In a normal query
   `x => x.Name == name` with `name == null` becomes `IS NULL`. As a typed string
   parameter EF Core keeps that meaning; section 10 proves the null row is the one
   returned. Guard: the placeholder type comes from the tree, so a null is passed as a
   typed null, never as an untyped constant the builder rewrote by hand. A null whose
   type the tree does not determine is refused. An empty string and a null stay
   distinct values.
5. **A delegate built for one model is used with another.** Two context types, or one
   context type whose model depends on instance state (for example a tenant-specific
   schema in `OnModelCreating`), must not share a delegate silently. Guard: the context
   type is in the key and the handle requires the context's runtime type to be exactly
   its `TContext`. The convenience methods key on the runtime type and provider name of
   the context in hand. A handle also records the `IModel` of the first context it runs
   against and throws if a later context of the same type presents a different model
   instance. Whether EF Core itself throws on a model mismatch is listed as assumed in
   section 11; the design does not rely on it.
6. **The specification changes while a call is running.** Guard: all values are read
   into the snapshot array synchronously at the start of the call, before the first
   `await`. Later mutation of the specification cannot change a query already built.
   The docs state that a specification instance must not be mutated during a call, as
   on the normal path.
7. **Numeric or date coercion shifts a comparison.** Lifting `(long)age` as an `int`
   would change overflow and comparison behaviour. Guard: the maximal captured subtree
   includes any conversion at its root, so the parameter type and the evaluated value
   are both post-conversion. No placeholder is ever narrowed or widened by the engine.
8. **`Skip`/`Take` baked in as constants from the first call.** Guard: they are always
   parameters when present, never constants, and their presence (not value) is in the
   key. Section 10 proves one delegate returns the right page for different values.
9. **A search pattern is altered in transit.** Casing or trimming a pattern would change
   `LIKE` results. Guard: the pattern string is passed through untouched as the string
   parameter. The engine builds the `EF.Functions.Like` call and nothing else.
10. **Tracking behaviour differs.** A compiled query with `Include` and no tracking flag
    must track the way the normal query does. Guard: the flag evaluators are applied in
    the tree in evaluator order, the flags are in the key, and no tracking call is added
    or dropped by the terminal step.
11. **The convenience cache grows without bound and an old delegate is evicted or
    swapped.** Guard: there is no eviction. The cache is a `ConcurrentDictionary` that
    only adds. Unbounded growth is a memory concern for a specification with unbounded
    shapes (a query tag built from user input is the realistic cause, since tags are in
    the key); it is not a correctness concern, because no entry is ever replaced. The
    docs tell users not to put unbounded values in query tags or include strings on
    compiled paths, and the engine exposes the cache count internally for tests. A
    bounded cache with eviction is a follow-up (section 12), not part of this design,
    because eviction policy is a product decision this design should not smuggle in.
12. **Concurrent first calls race to compile the same shape twice.** Guard: the cache
    stores a lazy invoker per key, so two racing calls may build the same delegate
    twice but cannot build two different delegates for one key; the second build is
    discarded. Building twice is wasted work, not wrong results.
13. **`PostProcessingAction` is silently dropped.** On the normal path, list methods
    apply it and first/single/count methods do not. Guard: the compiled list applies it,
    and the docs and XML comments state per method whether it applies, matching today's
    split exactly, so a user moving a call from `ListAsync` to `ListCompiledAsync` sees
    no change.

## 8. What to test

Tests go in `tests/OpenSpecification.EntityFrameworkCore.Tests`, following the existing
integration style (shared SQL Server, `flock` around `dotnet test` as AGENTS.md
requires). Unit tests for the engine need internals access; the test assembly already
has `InternalsVisibleTo` from the core package, and the EF Core project should add the
same for its internals if the engine stays internal.

Without a database:

- Parameter extraction, one case per form: constructor argument, specification field,
  specification property, computed capture (`age + 5`), method on a capture
  (`term.ToUpper()`), the same capture used twice (one placeholder), a capture inside a
  nested `Any`, a typed null, an enum, each numeric type, a `List<int>` and an `int[]`.
  Assert placeholder count, types in order, and extracted values.
- Refusals: `object` capture, entity capture, 16 parameters, non-empty `Items`,
  filtered include, unsupported collection type. Assert the exception type, and that
  the message names the feature.
- Shape key: pairs that differ in one thing only — a literal, a flag, a query tag, an
  include string, a Search group, `Skip` present versus absent, `Select` versus
  `SelectMany`, an order direction — produce different keys. Pairs that differ only in
  a captured value produce the same key. A hard-coded literal difference produces
  different keys.
- Handle validation: a template and an instance of the same class whose condition
  differs throw on the mismatched call.

Against SQL Server, each test runs the same specification through the normal path and
the compiled path and compares the rows, not just a count. Two empty results must not
pass as equal; seed data must include a row each query is expected to find and a row it
is expected to miss, following the evidence rule this repo's verify skill uses:

- Where with one, two and reused parameters; Where with a null string (assert the
  null-named row is returned); Where with an enum; Where with a `List<int>` of length
  0, 1 and 3 through one handle.
- Search with one group, with two groups, and with a pattern that only matches when
  passed through unaltered (mixed case and a `%` in the pattern).
- Order chains including `ThenByDescending`, with `Skip` and `Take` varied across calls
  on one handle, asserting different pages.
- Include (lambda and string) with `ThenInclude`, asserting the navigation is loaded;
  the same with `AsNoTracking`, asserting the returned entities are not tracked.
- `Select` with a captured value in the projection; `SelectMany`.
- `FirstOrDefaultCompiledAsync`, `SingleOrDefaultCompiledAsync` including the
  more-than-one-row exception, `CountCompiledAsync` and `AnyCompiledAsync` on a
  specification with `Skip` and `Take`, asserting paging does not change the count.
- `PostProcessingAction` applied by the list methods and not by count, on both paths.
- Repository and context-factory repository compiled methods, and the
  `ContextFactoryRepositoryBaseOfT` stream disposing its context per enumeration.
- A custom-evaluator repository throwing on its compiled methods and still working on
  its normal methods.
- Concurrency: many parallel calls with different values through one handle and through
  the convenience cache, asserting each caller gets its own rows.
- Multi-target: the whole compiled suite runs on net8.0, net9.0 and net10.0, because
  the overload set and the parameter limit come from the EF Core in use.

Performance is not a correctness gate, but one benchmark belongs in
`tests/OpenSpecification.Benchmarks` or a throwaway probe: the same query through
`ListAsync` and through a warmed `ListCompiledAsync`, reporting allocations. It exists
to catch a regression where the compiled path accidentally recompiles per call (for
example if extraction produced a new key per value). The test asserts behaviour, not a
timing threshold.

## 9. What to put in the docs

- A new page `docs/usage/use-compiled-queries.md`, next to
  [use-specification-dbcontext.md](../docs/usage/use-specification-dbcontext.md),
  with: what a compiled query buys (one translation, reused delegate) and what it does
  not (it does not make the database faster, and the first call for a shape is slower);
  the three call forms with the examples from section 3.5; the rule that a compiled
  call is terminal and cannot be composed with `WithSpecification`; and a "before you
  convert a specification" checklist taken from section 6.
- In that page, a short section on shapes: one delegate per shape, conditions create
  shapes, values do not, hard-coded literals create shapes, and query tags or include
  strings built from user input create unbounded shapes. Recommend the handle for a
  known hot query and the convenience methods elsewhere, and recommend one real call at
  startup for each shape that must be warm.
- XML documentation on every new public member, since the build treats missing comments
  and nullable warnings as errors. Each terminal method states whether it applies
  `PostProcessingAction` and that `Count`/`Any` evaluate criteria only.
- A `CHANGELOG.md` entry in the style of the 10.1.0 notes: the new types and methods,
  that no interface changed, that EF6 is unaffected, and the filtered-include refusal,
  stated plainly so a user with filtered includes knows before upgrading their calls.
- No change to `docs/usage/create-specifications.md`. Specifications are written the
  same way; that is the point of the design.

## 10. Claims proved by running code

Probes were small console projects under `/tmp` referencing this checkout's EF Core
project (EF Core 10.0.14, net10.0) and a `spec-sql` SQL Server. Each probe created a
database named `CompiledProbe...` with a generated suffix, seeded it, and dropped it at
the end. No repository test suite was run, and no library code was changed.

Proved:

1. **Strongly typed parameters are required.** A compiled query whose parameter was
   declared as `object` and cast inside the query failed at first run with
   `InvalidOperationException`: "Expression '@age' in the SQL tree does not have a type
   mapping assigned." The same query with an `int` parameter returned the right rows.
2. **A runtime-built expression compiles.** An
   `Expression<Func<Ctx, int, IQueryable<Customer>>>` assembled with expression trees
   (a `Set<Customer>()` call on a context parameter, a `Where` whose predicate compared
   against an `int` parameter) was passed to `EF.CompileAsyncQuery` through reflection
   (`MakeGenericMethod`). One delegate returned 2 rows for age 21 and 1 row for age 35,
   as expected. This is the exact mechanism the delegate factory in section 4.4 uses.
3. **The normal evaluator output cannot be compiled as it stands.** The expression
   returned by `SpecificationEvaluator.GetQuery` for `new ByAge(21)` is rooted at an EF
   Core entity query root (printed as a `Where` call over
   `Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression`), not at a context
   parameter. `EF.CompileAsyncQuery` needs the parameter form, so a separate builder is
   required.
4. **Captures are closure member accesses.** For `new ByAge(21)` the filter printed as
   `x => (x.Age > value(ByAge+<>c__DisplayClass0_0).age)`: a member access on a constant
   closure object. This is the shape the rewriter in section 4.2 is specified against.
5. **Scalar parameters work through one delegate with distinct values**: `int`
   (age), `Guid`, an enum (`Status.Active` matched 2 of 3 rows), a string captured in a
   `Select` projection (`"X-" + name` gave `X-A`, `X-C`), and a string pattern in
   `EF.Functions.Like` (`%A%` gave `A`, `%B%` gave `B` through one delegate).
6. **A typed null string parameter keeps normal semantics.** One delegate returned 1
   row for a null pattern (the row whose `Name` is null) and 1 row for `"A"`.
7. **`Skip` and `Take` work as `int` parameters.** One delegate with skip 1, take 1
   returned 1 row from 3.
8. **`List<int>` works as one parameter with varying length.** One delegate returned 1
   row for `[1]`, 3 rows for `[1,2,3]` and 0 rows for an empty list. An `int[]`
   parameter also worked. This is EF Core 10 on SQL Server; other providers are assumed,
   not proved (section 11).
9. **Terminals work inside a compiled query.** `Count` (2 for age 21), `FirstOrDefault`
   (returned the expected row), and a `FirstOrDefault` delegate built with the
   `CancellationToken` overload all returned correct results. `Include` combined with
   `AsNoTracking` and a `Where` returned the expected 2 rows.
10. **An entity parameter fails.** A compiled query taking a `Customer` parameter and
    comparing `x.Id == param.Id` threw `InvalidOperationException` when the query ran.
    The design refuses this shape earlier, with a clearer message.

## 11. Claims assumed, not proved

- The overload set, the 15-parameter ceiling, and collection parameter behaviour are
  proved only for EF Core 10.0.14 on SQL Server. EF Core 8.0.19 and 9.0.8 are assumed
  to offer the same overloads up to a possibly different ceiling, which is why the code
  discovers the overload at runtime and reports the limit it finds. This must be checked
  on each target before release; the multi-target test run in section 8 is where.
- Collection parameters on providers other than SQL Server (SQLite, PostgreSQL and so
  on) are assumed to behave as on SQL Server. If a provider rejects a collection
  parameter, EF Core throws at first run for that shape; the design does not catch and
  hide that, but the docs should name SQL Server as the tested provider until others
  are verified.
- EF Core compiled delegates are assumed thread-safe for concurrent invocation, as EF
  Core's own guidance to keep them in `static readonly` fields implies. The concurrency
  test in section 8 is the check.
- Whether EF Core translates a compiled query at delegate creation or at first
  invocation is assumed not to matter. The design pays any translation cost once per
  shape and terminal either way, and offers no fake warm-up call that would hit the
  database with dummy values.
- A context type whose model depends on instance state is assumed rare. The design
  throws when it detects a second model for one context type through a handle; it
  assumes EF Core would otherwise risk using the first model, and does not rely on EF
  Core throwing first.
- `DateOnly` and `TimeOnly` as parameters are assumed to work wherever EF Core maps
  them. They are in the supported list because their CLR types are scalars; no probe
  covered them.
- Performance benefit is assumed, not measured. Compiled queries remove query
  translation and delegate construction per call; how much that is worth depends on
  the query and the application. No number is claimed in the docs.

## 12. Alternatives considered and rejected

- **Compile inside `SpecificationEvaluator.GetQuery` and return an `IQueryable`.**
  Rejected as impossible, not merely awkward. A compiled delegate returns an
  `IAsyncEnumerable` or a `Task`, and its input expression must be rooted at a context
  parameter, while `GetQuery` receives and returns an `IQueryable` rooted at a query
  root (proved in section 10). An `IQueryable` wrapper that secretly materialised would
  break composition, lazy evaluation and tracking in ways a caller could not see.
- **One untyped entry point: pass all values as `object[]` into a delegate that takes a
  single params object.** Rejected on evidence. EF Core cannot assign a type mapping to
  an `object` parameter (section 10), so the delegate would have to be untyped and the
  benefit of a compiled query would be lost, or the query would fail at runtime.
- **A new authoring model: users write specifications as
  `Expression<Func<TContext, TParam, IQueryable<T>>>` factories or declare a parameter
  record per specification.** This would be strongly typed with no runtime rewriting,
  and it is what a user can already do by hand with `EF.CompileAsyncQuery` today.
  Rejected as the library feature because it abandons the existing specification
  classes, duplicates every query in two forms, and cannot reuse the evaluator order,
  the Search grouping or the include handling. It remains available to any user today,
  unchanged.
- **A source generator that emits a strongly typed compiled delegate per specification
  class.** Attractive later: no runtime rewriting, and the shapes are visible in
  generated code. Rejected for this design because a generator cannot run a
  specification's constructor to learn its shape, so it would need a new declarative
  surface anyway; it adds a generator package, build-time diagnostics and a debugging
  story to a library that has none. The runtime design can be replaced by generated
  delegates later without changing the public API in section 3, because the handle
  hides how its delegate was made.
- **Cache by specification type alone.** Rejected because it is the wrong-results case
  in section 7: a conditional specification of one class builds several queries, and a
  type-only key returns the first query for all of them, silently.
- **Fall back to the normal path when a shape is unsupported.** Rejected. A fallback
  would be correct but invisible: a user who asked for a compiled query and got a
  normal one would find out from a profiler, not from the library. Refusing with a
  named reason lets the user fix the specification or call the normal method
  deliberately.
- **Support synchronous compiled queries too (`EF.CompileQuery`).** Deferred, not
  rejected in principle. Every terminal method in the EF Core package is asynchronous,
  and `IAsyncEnumerable` has no synchronous compiled form. A sync surface can be added
  later behind the same handle without breaking this design.
- **Put a `Compile` method on `Specification<T>` in the core package.** Rejected. The
  core package targets netstandard2.0 and has no EF Core dependency; a compiled query is
  an EF Core concept bound to a `DbContext` type. The feature belongs in the EF Core
  package, keyed by context type, as designed.
- **Evict old delegates from the convenience cache (an LRU bound).** Deferred. Eviction
  is never a correctness issue here, and the right bound is a product decision. The
  design ships an unbounded cache, documents the unbounded-shape cause (tags and
  include strings built from user input), and leaves a bounded cache as a follow-up.

Follow-ups this design deliberately leaves out, in order: filtered includes on the
compiled path; caching the per-shape value extractors so computed captures cost no
per-call compile; a bounded convenience cache; synchronous terminals; verification on
SQLite and PostgreSQL providers.

## 13. Implementation outline

New files in `src/OpenSpecification.EntityFrameworkCore`:

- `Compiled/CompiledSpecification.cs` — the static factory, both handle classes, and
  `ParameterTypes`.
- `Compiled/CompiledSpecificationNotSupportedException.cs` — the exception in
  section 3.4.
- `Compiled/CompiledQueryEngine.cs` — internal: canonical builder, captured-value
  rewriter (build mode and extract mode), canonical shape printer, and the plan cache.
- `Compiled/CompiledDelegateFactory.cs` — internal: EF Core overload discovery, lambda
  construction, invoker construction through a generic engine class closed with
  `MakeGenericType`.
- `Compiled/DbContextCompiledExtensions.cs` — the methods in section 3.2 over the
  static plan cache.

Edited files, additions only:

- `RepositoryBaseOfT.cs` and `ContextFactoryRepositoryBaseOfT.cs` — the methods in
  section 3.3. Each checks its evaluator is the default before delegating to the engine,
  and the context-factory one creates and disposes contexts as its existing methods do.
- `OpenSpecification.EntityFrameworkCore.csproj` — an `InternalsVisibleTo` for the test
  assembly if the engine's unit tests need it, matching what the core project already
  does. No new package dependency: everything used is in the EF Core and BCL references
  the project already has.
- `docs/usage/use-compiled-queries.md` (new) and `CHANGELOG.md` (new entry).

No file in `src/OpenSpecification` or `src/OpenSpecification.EntityFramework6` changes.
No existing signature changes. The build's nullable and XML-comment warnings apply to
every new member, as they do today.

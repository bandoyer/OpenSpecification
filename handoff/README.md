# Handoff: compiled queries (#17)

Written 2026-11-09 by the chief thread on Dan's home machine, so Dan can continue on their work computer. This branch, `handoff/compiled-queries`, isn't meant to merge. Delete it once #17 has a decided design.

## What the next session does

Dan uses EF Core compiled queries a lot at work, and #17 asks OpenSpecification to support them. Three designs exist, and the choice between them depends on how Dan's work code uses compiled queries. Read Dan's work code with Dan, answer the questions under "What to find in the work code", then help Dan pick a design.

Rules for that session:

- The work code is Dan's employer's. Don't copy it, or anything that identifies it, into this repo, an issue, a PR, a commit or a comment on GitHub. Report patterns and counts to Dan in the thread, such as "12 compiled queries, 9 with one int parameter, 3 with optional filters", and use made-up names like `Customer` in any example.
- The work repos are on Azure DevOps. Don't push to them or open a PR there. Read only, unless Dan asks for something else.
- On bandoyer/OpenSpecification, never create an issue, comment or PR on ardalis/Specification, the upstream.
- Writing follows Dan's unslop rules. These include no em dashes, plain words, active voice, sentence-case headings and straight quotes.
- Use they/them for Dan.

## Where things stand

- OpenSpecification 10.1.0 shipped to nuget.org on 2026-11-02, with .NET 10 builds, paged results, the package icon and the EF6 runtimeconfig fix.
- No PR or run is open. No queue is running.
- Dan decided that NuGet audit advisories won't fail the build (#69 closed), and that in-memory paged results wait for a user to ask (#70 open).
- #17's body holds the first design and the results of a throwaway probe, which are in the comment under the issue. Dan said not to start building it, and that they need to review it more.
- A new public API is Dan's call under `skillflow.toml`'s `never-delegate` rule, so no run starts on #17 until Dan confirms the API.

## The three designs

- **#17 (the chief's).** The issue body. Your existing spec class is used unchanged. The library runs its constructor once with test values it picks, compares the queries, and turns what changed into the compiled query's parameter.
- **Sol (gpt-6.1-sol).** [design-sol.md](design-sol.md). You write a separate compiled spec whose constructor takes `QueryParameter<T>` markers.
- **Muse (muse-wonder-1.0).** [design-muse.md](design-muse.md). Your existing spec class is used unchanged, and you pass a new instance on each call. The library reads the values out of it each time, and caches one delegate per query shape.

Sol and Muse wrote theirs on 2026-11-08 without seeing #17. Their briefs had only the original upstream request and the repo's rules.

| | #17 | Sol | Muse |
|---|---|---|---|
| How you write the spec | Your existing class, unchanged | A separate constructor taking `QueryParameter<int>`, with `region.Value` inside expressions | Your existing class, unchanged |
| When the constructor runs | Once, with test values the library picks | Once, with markers whose getter always throws | On every call |
| Branches in the constructor | Refused when a test value reaches them, otherwise silently wrong | Can't happen, since reading a marker throws | Fine, since each combination of branches is its own shape |
| Values read from outside the arguments, such as a tenant id | Silently fixed at the first call | Refused | Read again on each call |
| Ways to get wrong rows with no error | Two | None found | Mainly a bug in the shape key |
| `Search` pattern, `Skip` and `Take` from a value | Refused | Refused in the first version | Supported |
| Cost of each call | One delegate call | One delegate call | Builds the spec, walks and prints the tree as a key, looks it up, and compiles a small expression for each computed value |
| API size | Static methods for list, first and count | A handle for list and stream | A handle with six operations, six `DbContext` extensions and new repository methods |
| Run against a real database | Yes, EF Core 10 | No | Yes, EF Core 10 |

Points the chief raised:

- **Muse's speedup is unproven, and may not exist.** EF already caches translated queries, so a compiled query saves only EF's cache lookup and expression building. Microsoft measures that at about 0.1 ms and 4 KB for a one-row query: 672 µs and 13 KB normally, 564 µs and 9 KB compiled. Muse's per-call work could cost as much. Benchmark it against a plain `ListAsync` before choosing it.
- **Sol's plan to build the query on a stand-in provider won't load includes.** EF's `Include` does nothing on a provider that isn't EF's own. #17's probe built the query on a real context, which works. Nothing in Sol's design has been run.
- **The chief's recommendation, if Dan picks Sol's way of writing specs:** Sol's marker model and strict capture check, #17's tested way of building and compiling the query, and from Muse a delegate per EF model and an exception that names the expression path. Add `Search`, `Skip` and `Take` from markers if marker-based builder overloads can carry them, since Muse showed EF accepts all three as parameters.

## The question for Dan

Option A is Sol's model. You write a second version of each spec you compile:

```csharp
public sealed class ActiveCustomersCompiledSpec : Specification<Customer>
{
    public ActiveCustomersCompiledSpec(QueryParameter<int> regionId)
    {
        Query.Where(c => c.RegionId == regionId.Value && c.IsActive)
             .OrderBy(c => c.Name)
             .AsNoTracking();
    }
}

static readonly CompiledSpecification<AppDbContext, Customer, int> ActiveByRegion =
    CompiledSpecification.Create<AppDbContext, Customer, int>(r => new ActiveCustomersCompiledSpec(r));

var customers = await ActiveByRegion.ToListAsync(db, 42, ct);
```

The `where` logic lives in two classes. An optional filter can't be an `if`, so each combination of filters is its own compiled spec. Each call costs only EF's delegate.

Option B is Muse's model. You keep the spec class you have:

```csharp
public sealed class ActiveCustomersSpec : Specification<Customer>
{
    public ActiveCustomersSpec(int regionId, string? nameStartsWith)
    {
        Query.Where(c => c.RegionId == regionId && c.IsActive)
             .OrderBy(c => c.Name)
             .AsNoTracking();

        if (!string.IsNullOrEmpty(nameStartsWith))
            Query.Search(c => c.Name, nameStartsWith + "%");
    }
}

static readonly CompiledSpecification<AppDbContext, Customer> Active =
    CompiledSpecification.Create<AppDbContext, Customer>(new ActiveCustomersSpec(0, null));

var customers = await Active.ToListAsync(db, new ActiveCustomersSpec(42, "Sm"), ct);
```

Nothing in the spec changes, but each call pays for the library's own work before EF runs, which may cancel the saving.

So: are Dan's compiled queries a few chosen hot queries, which fit Option A? Or would Dan switch many existing specs with no rewrite, especially ones with optional filters, which fit Option B after a benchmark?

## What to find in the work code

Count these across the compiled queries, without copying any code out:

1. How many compiled queries there are, and whether they're a few hot paths or many.
2. How many parameters each takes, and their types. Note any collection parameter, such as a list of ids for `Contains`.
3. How many have optional filters, and how they express them: separate compiled queries per combination, or a condition inside the query such as `(name == null || c.Name == name)`.
4. Whether any page with a `Skip` or `Take` that comes from a parameter.
5. What they return: lists, streams, one row, counts, any, or projections.
6. Whether they use `Include`, split queries, `AsNoTracking`, query filters or tags.
7. How they're organised: static fields on the `DbContext`, in repositories, or elsewhere, and whether specs from Ardalis.Specification or OpenSpecification exist next to them.
8. The EF Core version, whether the app uses `DbContext` pooling, and whether one context type ever runs with more than one model, such as per tenant.
9. Whether any compiled query reads a value from outside its parameters, such as a tenant id from the context.

Then recommend A or B to Dan, with the counts behind it, and update #17 once Dan decides. Rewrite its "New API", "Design" and "Limits" sections for the chosen design, and keep its probe results. Building and testing the library happens on the home machine, which has the `spec-sql` container and skillflow.

using Microsoft.EntityFrameworkCore;
using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike;

/// <summary>Runs each spec normally and compiled, and compares the rows.</summary>
public static class Checks
{
    private static int _failed;
    private static int _passed;

    public static async Task<int> RunAsync()
    {
        Db.Init();

        Console.WriteLine("Shapes:");
        Console.WriteLine($"  ById: {string.Join(" | ", Queries.ById.Shapes)}");
        Console.WriteLine($"  Search: {string.Join(" | ", Queries.Search.Shapes)}");
        Console.WriteLine($"  Details: {string.Join(" | ", Queries.Details.Shapes)}");
        Console.WriteLine($"  ByNickname: {string.Join(" | ", Queries.ByNickname.Shapes)}");
        Console.WriteLine();

        await Check("ById: alternating ids, with orders", async () =>
        {
            foreach (var id in new[] { 1, 2, 3, 999, 2, 5000, 1 })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerByIdSpec(id)).FirstOrDefaultAsync());
                var compiled = await Compiled(c => Queries.ById.FirstOrDefaultAsync(c, id));
                Same($"id {id}", Describe(normal), Describe(compiled));
            }
        });

        await Check("Search: every shape, alternating", async () =>
        {
            var cases = new (int Region, string? Prefix, CustomerSort Sort)[]
            {
                (3, "Smith", CustomerSort.Name), (3, null, CustomerSort.Newest), (3, "Smith", CustomerSort.Newest),
                (3, null, CustomerSort.Name), (7, "Jones", CustomerSort.Name), (3, "", CustomerSort.Name), (3, "Smith", CustomerSort.Name),
            };
            foreach (var (region, prefix, sort) in cases)
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerSearchSpec(region, prefix, sort)).ToListAsync());
                var compiled = await Compiled(c => Queries.Search.ToListAsync(c, region, prefix, sort));
                Same($"({region}, {prefix ?? "null"}, {sort})", Ids(normal), Ids(compiled));
            }
        });

        await Check("ById with .Value and with the conversion stripped", async () =>
        {
            foreach (var id in new[] { 4, 8, 4, 5000 })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerByIdSpec(id)).FirstOrDefaultAsync());
                var value = await Normal(c => c.Customers.WithSpecification(new CustomerByIdSpecValue(id)).FirstOrDefaultAsync());
                var stripped = await Normal(c => c.Customers.WithSpecification(new CustomerByIdSpecStripped(id)).FirstOrDefaultAsync());
                var compiledValue = await Compiled(c => Queries.ByIdValue.FirstOrDefaultAsync(c, id));
                var compiledStripped = await Compiled(c => Queries.ByIdStripped.FirstOrDefaultAsync(c, id));
                Same($"id {id} .Value normal", Describe(normal), Describe(value));
                Same($"id {id} stripped normal", Describe(normal), Describe(stripped));
                Same($"id {id} .Value compiled", Describe(normal), Describe(compiledValue));
                Same($"id {id} stripped compiled", Describe(normal), Describe(compiledStripped));
            }
        });

        await Check("Search with the conversion stripped: every shape", async () =>
        {
            foreach (var (prefix, sort) in new (string?, CustomerSort)[] { ("Smith", CustomerSort.Name), (null, CustomerSort.Newest), ("Smith", CustomerSort.Newest), (null, CustomerSort.Name) })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerSearchSpec(3, prefix, sort)).ToListAsync());
                var stripped = await Normal(c => c.Customers.WithSpecification(new CustomerSearchSpecStripped(3, prefix, sort)).ToListAsync());
                var compiled = await Compiled(c => Queries.SearchStripped.ToListAsync(c, 3, prefix, sort));
                Same($"({prefix ?? "null"}, {sort}) normal", Ids(normal), Ids(stripped));
                Same($"({prefix ?? "null"}, {sort}) compiled", Ids(normal), Ids(compiled));
            }
        });

        await Check("Search: count matches", async () =>
        {
            foreach (var prefix in new[] { "Smith", null })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerSearchSpec(3, prefix, CustomerSort.Name)).CountAsync());
                var compiled = await Compiled(c => Queries.Search.CountAsync(c, 3, prefix, CustomerSort.Name));
                Same($"prefix {prefix ?? "null"}", normal.ToString(), compiled.ToString());
            }
        });

        await Check("Names: projection with an id array of length 3, 0 and 1", async () =>
        {
            foreach (var ids in new[] { new[] { 1, 3, 5 }, Array.Empty<int>(), new[] { 2 } })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerNamesSpec(ids)).ToListAsync());
                var compiled = await Compiled(c => Queries.Names.ToListAsync(c, ids));
                Same($"[{string.Join(",", ids)}]", string.Join(",", normal), string.Join(",", compiled));
            }
        });

        await Check("Details: the readOnly variant changes tracking, ThenInclude loads", async () =>
        {
            foreach (var readOnly in new[] { true, false, true })
            {
                await using var context = Db.NewContext();
                var customer = await Queries.Details.FirstOrDefaultAsync(context, 7, readOnly);
                var tracked = context.ChangeTracker.Entries().Count();
                var lines = customer!.Orders.Sum(o => o.Lines.Count);
                if (readOnly != (tracked == 0)) throw new Exception($"readOnly {readOnly} tracked {tracked} entities");
                if (lines == 0) throw new Exception("order lines weren't loaded");
            }
        });

        await Check("ByNickname: null matches the null rows", async () =>
        {
            foreach (var nickname in new[] { null, "nick1", null, "missing" })
            {
                var normal = await Normal(c => c.Customers.WithSpecification(new CustomerByNicknameSpec(nickname)).ToListAsync());
                var compiled = await Compiled(c => Queries.ByNickname.ToListAsync(c, nickname));
                if (nickname is null && normal.Count == 0) throw new Exception("the normal path found no null rows, so this proves nothing");
                Same($"nickname {nickname ?? "null"}", Ids(normal), Ids(compiled));
            }
        });

        await Check("FixedSearch: a fixed Search term passes the capture check", async () =>
        {
            var normal = await Normal(c => c.Customers.WithSpecification(new CustomerFixedSearchSpec(3)).ToListAsync());
            var compiled = await Compiled(c => Queries.FixedSearch.ToListAsync(c, 3));
            if (normal.Count == 0) throw new Exception("no rows, so this proves nothing");
            Same("region 3", Ids(normal), Ids(compiled));
        });

        await Check("A second model gets its own delegate", async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                await using var spike = Db.NewContext();
                await using var other = Db.NewOtherContext();
                var a = await Queries.ById.FirstOrDefaultAsync(spike, 10 + i);
                var b = await Queries.ById.FirstOrDefaultAsync(other, 20 + i);
                if (a?.Id != 10 + i || b?.Id != 20 + i) throw new Exception($"got {a?.Id} and {b?.Id}");
            }

            if (Queries.ById.ModelsBuilt != 2) throw new Exception($"expected 2 delegates, built {Queries.ById.ModelsBuilt}");
        });

        await Check("Parallel calls each get their own rows", async () =>
        {
            var results = await Task.WhenAll(Enumerable.Range(1, 200).Select(async i =>
            {
                await using var context = Db.NewContext();
                var region = i % 10;
                var prefix = i % 3 == 0 ? null : "Smith";
                var sort = i % 2 == 0 ? CustomerSort.Name : CustomerSort.Newest;
                var rows = await Queries.Search.ToListAsync(context, region, prefix, sort);
                var expected = await Normal(c => c.Customers.WithSpecification(new CustomerSearchSpec(region, prefix, sort)).ToListAsync());
                return Ids(rows) == Ids(expected);
            }));
            if (results.Any(ok => !ok)) throw new Exception($"{results.Count(ok => !ok)} of {results.Length} calls got other rows");
        });

        Console.WriteLine();
        Console.WriteLine("Refusals:");

        await Refused("A guard on an int argument", () =>
            CompiledSpecification.Create<Customer, int>(m => new CustomersAboveIdSpec(m)));

        await Refused("A static value inside the query", () =>
            Warm(CompiledSpecification.Create<Customer, int>(r => new CustomersForTenantSpec(r))));

        await Refused("A local copied from outside state", () =>
            Warm(CompiledSpecification.Create<Customer, int>(r => new CustomersForTenantLocalSpec(r))));

        await Refused("Arithmetic on an argument", () =>
            Warm(CompiledSpecification.Create<Customer, int>(id => new NextCustomerSpec(id))));

        await Refused("A [Flags] enum used as a variant", () =>
            CompiledSpecification.Create<Customer, Permissions>(p => new CustomersWithPermissionSpec(p)));

        await Refused("A PostProcessingAction", () =>
            CompiledSpecification.Create<Customer, int>(r => new PostProcessedSpec(r)));

        var fixedRegion = new QueryArg<int>(4);
        await Refused("A factory that captures a value", () =>
            CompiledSpecification.Create<Customer, int>(r => new TwoRegionSpec(r, fixedRegion)));

        await Refused("A QueryArg with a fixed value, captured from a static field", () =>
            Warm(CompiledSpecification.Create<Customer, int>(r => new TwoRegionSpec(r, StaticArgs.Region))));

        Console.WriteLine();
        Console.WriteLine("Known limit:");
        await KnownLimit();

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static class StaticArgs
    {
        public static readonly QueryArg<int> Region = 4;
    }

    private static async Task KnownLimit()
    {
        FeatureFlags.OnlyActive = false;
        var handle = CompiledSpecification.Create<Customer, int>(r => new ActiveCustomersSpec(r));
        FeatureFlags.OnlyActive = true;

        var normal = await Normal(c => c.Customers.WithSpecification(new ActiveCustomersSpec(6)).ToListAsync());
        var compiled = await Compiled(c => handle.ToListAsync(c, 6));
        FeatureFlags.OnlyActive = false;

        Console.WriteLine($"  A branch on a static flag: normal {normal.Count} rows, compiled {compiled.Count} rows. " +
            (normal.Count == compiled.Count ? "Same, unexpectedly." : "The compiled query kept the shape from when it was created, as expected."));
    }

    private static void Warm<TResult, T1>(CompiledSpecification<TResult, T1> handle)
    {
        using var context = Db.NewContext();
        handle.Warm(context);
    }

    private static async Task Check(string name, Func<Task> body)
    {
        try
        {
            await body();
            _passed++;
            Console.WriteLine($"  PASS {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Task Refused(string name, Action body)
    {
        try
        {
            body();
            _failed++;
            Console.WriteLine($"  FAIL {name}: not refused");
        }
        catch (CompiledSpecificationNotSupportedException ex)
        {
            _passed++;
            Console.WriteLine($"  PASS {name}\n       {ex.Message}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL {name}: threw {ex.GetType().Name}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static async Task<T> Normal<T>(Func<SpikeContext, Task<T>> query)
    {
        await using var context = Db.NewContext();
        return await query(context);
    }

    private static async Task<T> Compiled<T>(Func<SpikeContext, Task<T>> query)
    {
        await using var context = Db.NewContext();
        return await query(context);
    }

    private static string Ids(IEnumerable<Customer> rows) => string.Join(",", rows.Select(c => c.Id));

    private static string Describe(Customer? c) => c is null ? "null" : $"{c.Id}:{c.Name}/{c.Orders.Count} orders";

    private static void Same(string label, string normal, string compiled)
    {
        if (normal != compiled) throw new Exception($"{label}: normal [{Trim(normal)}] but compiled [{Trim(compiled)}]");
    }

    private static string Trim(string s) => s.Length > 120 ? s[..120] + "..." : s;
}

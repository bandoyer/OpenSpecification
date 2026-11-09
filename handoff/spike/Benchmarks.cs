using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike;

public class SpikeConfig : ManualConfig
{
    public SpikeConfig()
    {
        Add(DefaultConfig.Instance);
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(5)
            .WithIterationCount(20));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}

/// <summary>One customer by id, with its orders, no tracking.</summary>
[Config(typeof(SpikeConfig))]
public class ByIdBenchmarks
{
    private static readonly Func<DbContext, int, CancellationToken, Task<Customer?>> _handWritten =
        EF.CompileAsyncQuery((DbContext c, int id, CancellationToken ct) =>
            c.Set<Customer>().Where(x => x.Id == id).Include(x => x.Orders).AsNoTracking().FirstOrDefault());

    private PooledDbContextFactory<SpikeContext> _factory = null!;
    private readonly int _id = 421;

    [GlobalSetup]
    public void Setup()
    {
        Db.Init();
        _factory = new PooledDbContextFactory<SpikeContext>(Db.Options);
    }

    [Benchmark(Baseline = true)]
    public async Task<Customer?> PlainEf()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.Where(x => x.Id == _id).Include(x => x.Orders).AsNoTracking().FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Customer?> SpecToday()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerByIdSpecToday(_id)).FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Customer?> SpecQueryArgNormalPath()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerByIdSpec(_id)).FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Customer?> SpecQueryArgValueNormalPath()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerByIdSpecValue(_id)).FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Customer?> SpecQueryArgStrippedNormalPath()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerByIdSpecStripped(_id)).FirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<Customer?> HandWrittenCompiled()
    {
        await using var c = _factory.CreateDbContext();
        return await _handWritten(c, _id, default);
    }

    [Benchmark]
    public async Task<Customer?> CompiledSpec()
    {
        await using var c = _factory.CreateDbContext();
        return await Queries.ById.FirstOrDefaultAsync(c, _id);
    }
}

/// <summary>A region's customers, with an optional name prefix and a sort the caller picks. About 14 rows.</summary>
[Config(typeof(SpikeConfig))]
public class SearchBenchmarks
{
    private static readonly Func<DbContext, int, string, IAsyncEnumerable<Customer>> _prefixByName =
        EF.CompileAsyncQuery((DbContext c, int r, string p) =>
            c.Set<Customer>().Where(x => x.RegionId == r).Where(x => x.Name.StartsWith(p)).OrderBy(x => x.Name).AsNoTracking());

    private static readonly Func<DbContext, int, string, IAsyncEnumerable<Customer>> _prefixNewest =
        EF.CompileAsyncQuery((DbContext c, int r, string p) =>
            c.Set<Customer>().Where(x => x.RegionId == r).Where(x => x.Name.StartsWith(p)).OrderByDescending(x => x.CreatedOn).AsNoTracking());

    private static readonly Func<DbContext, int, IAsyncEnumerable<Customer>> _allByName =
        EF.CompileAsyncQuery((DbContext c, int r) =>
            c.Set<Customer>().Where(x => x.RegionId == r).OrderBy(x => x.Name).AsNoTracking());

    private static readonly Func<DbContext, int, IAsyncEnumerable<Customer>> _allNewest =
        EF.CompileAsyncQuery((DbContext c, int r) =>
            c.Set<Customer>().Where(x => x.RegionId == r).OrderByDescending(x => x.CreatedOn).AsNoTracking());

    private PooledDbContextFactory<SpikeContext> _factory = null!;
    private readonly int _region = 3;
    private readonly string? _prefix = "Smith";
    private readonly CustomerSort _sort = CustomerSort.Name;

    [GlobalSetup]
    public void Setup()
    {
        Db.Init();
        _factory = new PooledDbContextFactory<SpikeContext>(Db.Options);
    }

    [Benchmark(Baseline = true)]
    public async Task<List<Customer>> PlainEf()
    {
        await using var c = _factory.CreateDbContext();
        IQueryable<Customer> query = c.Customers.Where(x => x.RegionId == _region);
        if (_prefix is not null) query = query.Where(x => x.Name.StartsWith(_prefix));
        query = _sort == CustomerSort.Newest ? query.OrderByDescending(x => x.CreatedOn) : query.OrderBy(x => x.Name);
        return await query.AsNoTracking().ToListAsync();
    }

    [Benchmark]
    public async Task<List<Customer>> SpecToday()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerSearchSpecToday(_region, _prefix, _sort)).ToListAsync();
    }

    [Benchmark]
    public async Task<List<Customer>> SpecQueryArgNormalPath()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerSearchSpec(_region, _prefix, _sort)).ToListAsync();
    }

    [Benchmark]
    public async Task<List<Customer>> SpecQueryArgStrippedNormalPath()
    {
        await using var c = _factory.CreateDbContext();
        return await c.Customers.WithSpecification(new CustomerSearchSpecStripped(_region, _prefix, _sort)).ToListAsync();
    }

    [Benchmark]
    public async Task<List<Customer>> HandWrittenCompiled()
    {
        await using var c = _factory.CreateDbContext();
        var list = new List<Customer>();
        await foreach (var row in Pick(c)) list.Add(row);
        return list;
    }

    // The same query behind a method that returns Task<List<T>>, as the handle's ToListAsync is.
    [Benchmark]
    public async Task<List<Customer>> HandWrittenCompiledWrapped()
    {
        await using var c = _factory.CreateDbContext();
        return await Collect(Pick(c), default);
    }

    private IAsyncEnumerable<Customer> Pick(DbContext c) => _prefix is null
        ? (_sort == CustomerSort.Newest ? _allNewest(c, _region) : _allByName(c, _region))
        : (_sort == CustomerSort.Newest ? _prefixNewest(c, _region, _prefix) : _prefixByName(c, _region, _prefix));

    private static async Task<List<Customer>> Collect(IAsyncEnumerable<Customer> rows, CancellationToken cancellationToken)
    {
        var list = new List<Customer>();
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false)) list.Add(row);
        return list;
    }

    [Benchmark]
    public async Task<List<Customer>> CompiledSpec()
    {
        await using var c = _factory.CreateDbContext();
        return await Queries.Search.ToListAsync(c, _region, _prefix, _sort);
    }
}

/// <summary>
/// Building the search spec, with no database. It isolates what the core builder's scan would cost every spec, and
/// what stripping costs a spec that opted in.
/// </summary>
[Config(typeof(SpikeConfig))]
public class SpecConstructionBenchmarks
{
    private readonly int _region = 3;
    private readonly string? _prefix = "Smith";
    private readonly CustomerSort _sort = CustomerSort.Name;

    [Benchmark(Baseline = true)]
    public object Today() => new CustomerSearchSpecToday(_region, _prefix, _sort);

    [Benchmark]
    public object TodayScanned() => new CustomerSearchSpecTodayScanned(_region, _prefix, _sort);

    [Benchmark]
    public object QueryArgConversion() => new CustomerSearchSpec(_region, _prefix, _sort);

    [Benchmark]
    public object QueryArgStripped() => new CustomerSearchSpecStripped(_region, _prefix, _sort);
}

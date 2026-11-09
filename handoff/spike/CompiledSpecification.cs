using Microsoft.EntityFrameworkCore;
using OpenSpecification.Spike.Engine;

namespace OpenSpecification.EntityFrameworkCore;

public sealed class CompiledSpecificationNotSupportedException : NotSupportedException
{
    public CompiledSpecificationNotSupportedException(string message) : base(message) { }
    public CompiledSpecificationNotSupportedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Creates compiled specifications. The spike covers one to three arguments.</summary>
public static class CompiledSpecification
{
    public static CompiledSpecification<T, T1> Create<T, T1>(Func<QueryArg<T1>, ISpecification<T>> factory) where T : class
        => new(CompiledCore<T, T>.Create(factory, [typeof(T1)], r => factory(new(r.Markers[0])), projected: false));

    public static CompiledSpecification<TResult, T1> Create<T, TResult, T1>(Func<QueryArg<T1>, ISpecification<T, TResult>> factory) where T : class
        => new(CompiledCore<T, TResult>.Create(factory, [typeof(T1)], r => factory(new(r.Markers[0])), projected: true));

    public static CompiledSpecification<T, T1, T2> Create<T, T1, T2>(Func<QueryArg<T1>, QueryArg<T2>, ISpecification<T>> factory) where T : class
        => new(CompiledCore<T, T>.Create(factory, [typeof(T1), typeof(T2)], r => factory(new(r.Markers[0]), new(r.Markers[1])), projected: false));

    public static CompiledSpecification<T, T1, T2, T3> Create<T, T1, T2, T3>(Func<QueryArg<T1>, QueryArg<T2>, QueryArg<T3>, ISpecification<T>> factory) where T : class
        => new(CompiledCore<T, T>.Create(factory, [typeof(T1), typeof(T2), typeof(T3)], r => factory(new(r.Markers[0]), new(r.Markers[1]), new(r.Markers[2])), projected: false));
}

public sealed class CompiledSpecification<TResult, T1>
{
    private readonly ICompiledCore _core;
    private readonly Func<T1, int>? _select;
    private readonly ModelCache<Func<DbContext, T1, IAsyncEnumerable<TResult>>>[] _list;
    private readonly ModelCache<Func<DbContext, T1, CancellationToken, Task<TResult>>>[] _first;
    private readonly ModelCache<Func<DbContext, T1, CancellationToken, Task<int>>>[] _count;

    internal CompiledSpecification(ICompiledCore core)
    {
        _core = core;
        _select = (Func<T1, int>?)core.Selector;
        _list = Caches<Func<DbContext, T1, IAsyncEnumerable<TResult>>>(Operation.List);
        _first = Caches<Func<DbContext, T1, CancellationToken, Task<TResult>>>(Operation.First);
        _count = Caches<Func<DbContext, T1, CancellationToken, Task<int>>>(Operation.Count);
    }

    public IReadOnlyList<string> Shapes => _core.ShapeDescriptions;
    internal int ModelsBuilt => _list.Sum(c => c.Count) + _first.Sum(c => c.Count) + _count.Sum(c => c.Count);

    public async Task<List<TResult>> ToListAsync(DbContext context, T1 arg1, CancellationToken cancellationToken = default)
    {
        var list = new List<TResult>();
        await foreach (var item in AsAsyncEnumerable(context, arg1).WithCancellation(cancellationToken).ConfigureAwait(false)) list.Add(item);
        return list;
    }

    public IAsyncEnumerable<TResult> AsAsyncEnumerable(DbContext context, T1 arg1)
        => _list[Index(arg1)].Get(context)(context, arg1);

    public Task<TResult?> FirstOrDefaultAsync(DbContext context, T1 arg1, CancellationToken cancellationToken = default)
        => _first[Index(arg1)].Get(context)(context, arg1, cancellationToken)!;

    public Task<int> CountAsync(DbContext context, T1 arg1, CancellationToken cancellationToken = default)
        => _count[Index(arg1)].Get(context)(context, arg1, cancellationToken);

    /// <summary>Builds the list delegate of every shape for this context's model, so errors show up at startup.</summary>
    public void Warm(DbContext context)
    {
        foreach (var cache in _list) cache.Get(context);
    }

    private int Index(T1 arg1) => _select is null ? 0 : _select(arg1);

    private ModelCache<TDelegate>[] Caches<TDelegate>(Operation operation) where TDelegate : class
        => Enumerable.Range(0, _core.ShapeCount).Select(i => new ModelCache<TDelegate>(ctx => (TDelegate)(object)_core.Build(i, operation, ctx))).ToArray();
}

public sealed class CompiledSpecification<TResult, T1, T2>
{
    private readonly ICompiledCore _core;
    private readonly Func<T1, T2, int>? _select;
    private readonly ModelCache<Func<DbContext, T1, T2, IAsyncEnumerable<TResult>>>[] _list;
    private readonly ModelCache<Func<DbContext, T1, T2, CancellationToken, Task<TResult>>>[] _first;
    private readonly ModelCache<Func<DbContext, T1, T2, CancellationToken, Task<int>>>[] _count;

    internal CompiledSpecification(ICompiledCore core)
    {
        _core = core;
        _select = (Func<T1, T2, int>?)core.Selector;
        _list = Caches<Func<DbContext, T1, T2, IAsyncEnumerable<TResult>>>(Operation.List);
        _first = Caches<Func<DbContext, T1, T2, CancellationToken, Task<TResult>>>(Operation.First);
        _count = Caches<Func<DbContext, T1, T2, CancellationToken, Task<int>>>(Operation.Count);
    }

    public IReadOnlyList<string> Shapes => _core.ShapeDescriptions;

    public async Task<List<TResult>> ToListAsync(DbContext context, T1 arg1, T2 arg2, CancellationToken cancellationToken = default)
    {
        var list = new List<TResult>();
        await foreach (var item in AsAsyncEnumerable(context, arg1, arg2).WithCancellation(cancellationToken).ConfigureAwait(false)) list.Add(item);
        return list;
    }

    public IAsyncEnumerable<TResult> AsAsyncEnumerable(DbContext context, T1 arg1, T2 arg2)
        => _list[Index(arg1, arg2)].Get(context)(context, arg1, arg2);

    public Task<TResult?> FirstOrDefaultAsync(DbContext context, T1 arg1, T2 arg2, CancellationToken cancellationToken = default)
        => _first[Index(arg1, arg2)].Get(context)(context, arg1, arg2, cancellationToken)!;

    public Task<int> CountAsync(DbContext context, T1 arg1, T2 arg2, CancellationToken cancellationToken = default)
        => _count[Index(arg1, arg2)].Get(context)(context, arg1, arg2, cancellationToken);

    public void Warm(DbContext context)
    {
        foreach (var cache in _list) cache.Get(context);
    }

    private int Index(T1 arg1, T2 arg2) => _select is null ? 0 : _select(arg1, arg2);

    private ModelCache<TDelegate>[] Caches<TDelegate>(Operation operation) where TDelegate : class
        => Enumerable.Range(0, _core.ShapeCount).Select(i => new ModelCache<TDelegate>(ctx => (TDelegate)(object)_core.Build(i, operation, ctx))).ToArray();
}

public sealed class CompiledSpecification<TResult, T1, T2, T3>
{
    private readonly ICompiledCore _core;
    private readonly Func<T1, T2, T3, int>? _select;
    private readonly ModelCache<Func<DbContext, T1, T2, T3, IAsyncEnumerable<TResult>>>[] _list;
    private readonly ModelCache<Func<DbContext, T1, T2, T3, CancellationToken, Task<TResult>>>[] _first;
    private readonly ModelCache<Func<DbContext, T1, T2, T3, CancellationToken, Task<int>>>[] _count;

    internal CompiledSpecification(ICompiledCore core)
    {
        _core = core;
        _select = (Func<T1, T2, T3, int>?)core.Selector;
        _list = Caches<Func<DbContext, T1, T2, T3, IAsyncEnumerable<TResult>>>(Operation.List);
        _first = Caches<Func<DbContext, T1, T2, T3, CancellationToken, Task<TResult>>>(Operation.First);
        _count = Caches<Func<DbContext, T1, T2, T3, CancellationToken, Task<int>>>(Operation.Count);
    }

    public IReadOnlyList<string> Shapes => _core.ShapeDescriptions;

    public async Task<List<TResult>> ToListAsync(DbContext context, T1 arg1, T2 arg2, T3 arg3, CancellationToken cancellationToken = default)
    {
        var list = new List<TResult>();
        await foreach (var item in AsAsyncEnumerable(context, arg1, arg2, arg3).WithCancellation(cancellationToken).ConfigureAwait(false)) list.Add(item);
        return list;
    }

    public IAsyncEnumerable<TResult> AsAsyncEnumerable(DbContext context, T1 arg1, T2 arg2, T3 arg3)
        => _list[Index(arg1, arg2, arg3)].Get(context)(context, arg1, arg2, arg3);

    public Task<TResult?> FirstOrDefaultAsync(DbContext context, T1 arg1, T2 arg2, T3 arg3, CancellationToken cancellationToken = default)
        => _first[Index(arg1, arg2, arg3)].Get(context)(context, arg1, arg2, arg3, cancellationToken)!;

    public Task<int> CountAsync(DbContext context, T1 arg1, T2 arg2, T3 arg3, CancellationToken cancellationToken = default)
        => _count[Index(arg1, arg2, arg3)].Get(context)(context, arg1, arg2, arg3, cancellationToken);

    public void Warm(DbContext context)
    {
        foreach (var cache in _list) cache.Get(context);
    }

    private int Index(T1 arg1, T2 arg2, T3 arg3) => _select is null ? 0 : _select(arg1, arg2, arg3);

    private ModelCache<TDelegate>[] Caches<TDelegate>(Operation operation) where TDelegate : class
        => Enumerable.Range(0, _core.ShapeCount).Select(i => new ModelCache<TDelegate>(ctx => (TDelegate)(object)_core.Build(i, operation, ctx))).ToArray();
}

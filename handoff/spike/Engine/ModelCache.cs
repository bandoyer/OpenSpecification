using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OpenSpecification.Spike.Engine;

/// <summary>
/// One EF compiled delegate per model, since EF throws when a compiled query meets a second model. The last model's
/// entry is published as one object, so the usual call is a reference comparison.
/// </summary>
internal sealed class ModelCache<TDelegate>(Func<DbContext, TDelegate> build) where TDelegate : class
{
    private sealed record Entry(IModel Model, TDelegate Delegate);

    private readonly ConditionalWeakTable<IModel, TDelegate> _all = new();
    private readonly object _lock = new();
    private Entry? _last;

    public int Count => _all.Count();

    public TDelegate Get(DbContext context)
    {
        var model = context.Model;
        var last = Volatile.Read(ref _last);
        return last is not null && ReferenceEquals(last.Model, model) ? last.Delegate : GetSlow(context, model);
    }

    private TDelegate GetSlow(DbContext context, IModel model)
    {
        if (!_all.TryGetValue(model, out var built))
        {
            lock (_lock)
            {
                if (!_all.TryGetValue(model, out built))
                {
                    built = build(context);
                    _all.Add(model, built);
                }
            }
        }

        Volatile.Write(ref _last, new Entry(model, built));
        return built;
    }
}

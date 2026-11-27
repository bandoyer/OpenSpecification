using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using OpenSpecification;

namespace GroupByApiPrototype;

/// <summary>
/// Existentially hides TKey and TOrderKey behind a 10.x-compatible additive seam.
/// In production, Specification&lt;T, TResult&gt; would implement an additive IGroupedSpecification&lt;T, TResult&gt;
/// interface exposing this plan (no changes to ISpecification&lt;T, TResult&gt; or IReadRepositoryBase&lt;T&gt;).
/// </summary>
public interface IGroupByPlan<T, TResult>
{
    Type KeyType { get; }
    bool HasResultSelector { get; }
    int? GroupSkip { get; set; }
    int? GroupTake { get; set; }
    bool IsPostGroupDistinct { get; set; }

    IQueryable<TResult> Apply(IQueryable<T> criteriaSource);
    int CountGroups(IQueryable<T> criteriaSource);
    IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource);
    string Describe();
}

public interface IGroupOrderStep<T, TKey>
{
    bool IsDescending { get; }
    bool IsThenBy { get; }
    IOrderedQueryable<IGrouping<TKey, T>> ApplyPrimary(IQueryable<IGrouping<TKey, T>> source);
    IOrderedQueryable<IGrouping<TKey, T>> ApplyThen(IOrderedQueryable<IGrouping<TKey, T>> source);
    IOrderedEnumerable<IGrouping<TKey, T>> ApplyPrimary(IEnumerable<IGrouping<TKey, T>> source);
    IOrderedEnumerable<IGrouping<TKey, T>> ApplyThen(IOrderedEnumerable<IGrouping<TKey, T>> source);
    string Describe();
}

/// <summary>
/// Preserves strongly typed TOrderKey so EF6 NEVER sees ExpressionType.Convert to object (no boxing!).
/// Zero reflection at query execution time.
/// </summary>
public sealed class GroupOrderStep<T, TKey, TOrderKey>(
    Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
    bool isDescending,
    bool isThenBy) : IGroupOrderStep<T, TKey>
{
    private Func<IGrouping<TKey, T>, TOrderKey>? _compiled;
    private Func<IGrouping<TKey, T>, TOrderKey> Compiled => _compiled ??= keySelector.Compile();

    public bool IsDescending { get; } = isDescending;
    public bool IsThenBy { get; } = isThenBy;

    public IOrderedQueryable<IGrouping<TKey, T>> ApplyPrimary(IQueryable<IGrouping<TKey, T>> source)
        => IsDescending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);

    public IOrderedQueryable<IGrouping<TKey, T>> ApplyThen(IOrderedQueryable<IGrouping<TKey, T>> source)
        => IsDescending ? source.ThenByDescending(keySelector) : source.ThenBy(keySelector);

    public IOrderedEnumerable<IGrouping<TKey, T>> ApplyPrimary(IEnumerable<IGrouping<TKey, T>> source)
        => IsDescending ? source.OrderByDescending(Compiled) : source.OrderBy(Compiled);

    public IOrderedEnumerable<IGrouping<TKey, T>> ApplyThen(IOrderedEnumerable<IGrouping<TKey, T>> source)
        => IsDescending ? source.ThenByDescending(Compiled) : source.ThenBy(Compiled);

    public string Describe()
        => $"{(IsThenBy ? "ThenBy" : "OrderBy")}{(IsDescending ? "Descending" : "")}<{typeof(TOrderKey).Name}>({keySelector})";
}

/// <summary>
/// Strongly typed group plan for IGrouping-stage operations (Candidates A, B, D).
/// </summary>
public sealed class GroupingStagePlan<T, TKey, TResult>(Expression<Func<T, TKey>> keySelector)
    : IGroupByPlan<T, TResult>
{
    public Expression<Func<T, TKey>> KeySelector { get; } = keySelector;
    public List<Expression<Func<IGrouping<TKey, T>, bool>>> HavingPredicates { get; } = [];
    public List<IGroupOrderStep<T, TKey>> OrderSteps { get; } = [];
    public Expression<Func<IGrouping<TKey, T>, TResult>>? ResultSelector { get; set; }

    public Type KeyType => typeof(TKey);
    public bool HasResultSelector => ResultSelector is not null;
    public int? GroupSkip { get; set; }
    public int? GroupTake { get; set; }
    public bool IsPostGroupDistinct { get; set; }

    public IQueryable<TResult> Apply(IQueryable<T> criteriaSource)
    {
        if (ResultSelector is null)
            throw new SelectorNotFoundException();

        var grouped = ApplyGroupAndHaving(criteriaSource);
        if (OrderSteps.Count > 0)
        {
            IOrderedQueryable<IGrouping<TKey, T>>? ordered = null;
            foreach (var step in OrderSteps)
            {
                ordered = ordered is null || !step.IsThenBy
                    ? step.ApplyPrimary(grouped)
                    : step.ApplyThen(ordered);
            }
            grouped = ordered!;
        }

        IQueryable<TResult> projected = grouped.Select(ResultSelector);
        if (IsPostGroupDistinct) projected = projected.Distinct();
        if (GroupSkip is int skip) projected = projected.Skip(skip);
        if (GroupTake is int take) projected = projected.Take(take);
        return projected;
    }

    public int CountGroups(IQueryable<T> criteriaSource)
    {
        if (ResultSelector is null)
            throw new SelectorNotFoundException();

        var grouped = ApplyGroupAndHaving(criteriaSource);
        return IsPostGroupDistinct
            ? grouped.Select(ResultSelector).Distinct().Count()
            : grouped.Count();
    }

    public IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource)
    {
        if (ResultSelector is null)
            throw new SelectorNotFoundException();

        IEnumerable<IGrouping<TKey, T>> grouped = criteriaSource.GroupBy(KeySelector.Compile());
        foreach (var having in HavingPredicates)
            grouped = grouped.Where(having.Compile());

        if (OrderSteps.Count > 0)
        {
            IOrderedEnumerable<IGrouping<TKey, T>>? ordered = null;
            foreach (var step in OrderSteps)
            {
                ordered = ordered is null || !step.IsThenBy
                    ? step.ApplyPrimary(grouped)
                    : step.ApplyThen(ordered);
            }
            grouped = ordered!;
        }

        IEnumerable<TResult> projected = grouped.Select(ResultSelector.Compile());
        if (IsPostGroupDistinct) projected = projected.Distinct();
        if (GroupSkip is int skip) projected = projected.Skip(skip);
        if (GroupTake is int take) projected = projected.Take(take);
        return projected;
    }

    private IQueryable<IGrouping<TKey, T>> ApplyGroupAndHaving(IQueryable<T> criteriaSource)
    {
        var grouped = criteriaSource.GroupBy(KeySelector);
        foreach (var having in HavingPredicates)
            grouped = grouped.Where(having);
        return grouped;
    }

    public string Describe()
    {
        var parts = new List<string> { $"GroupBy<{typeof(TKey).Name}>({KeySelector})" };
        parts.AddRange(HavingPredicates.Select(h => $"Having({h})"));
        parts.AddRange(OrderSteps.Select(o => o.Describe()));
        parts.Add(ResultSelector is not null ? $"Select({ResultSelector})" : "Select(<MISSING!>)");
        if (IsPostGroupDistinct) parts.Add("Distinct()");
        if (GroupSkip is int s) parts.Add($"Skip({s})");
        if (GroupTake is int t) parts.Add($"Take({t})");
        return string.Join(" -> ", parts);
    }
}

public interface IResultOrderStep<TResult>
{
    bool IsDescending { get; }
    bool IsThenBy { get; }
    IOrderedQueryable<TResult> ApplyPrimary(IQueryable<TResult> source);
    IOrderedQueryable<TResult> ApplyThen(IOrderedQueryable<TResult> source);
    IOrderedEnumerable<TResult> ApplyPrimary(IEnumerable<TResult> source);
    IOrderedEnumerable<TResult> ApplyThen(IOrderedEnumerable<TResult> source);
    string Describe();
}

public sealed class ResultOrderStep<TResult, TOrderKey>(
    Expression<Func<TResult, TOrderKey>> keySelector,
    bool isDescending,
    bool isThenBy) : IResultOrderStep<TResult>
{
    private Func<TResult, TOrderKey>? _compiled;
    private Func<TResult, TOrderKey> Compiled => _compiled ??= keySelector.Compile();

    public bool IsDescending { get; } = isDescending;
    public bool IsThenBy { get; } = isThenBy;

    public IOrderedQueryable<TResult> ApplyPrimary(IQueryable<TResult> source)
        => IsDescending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
    public IOrderedQueryable<TResult> ApplyThen(IOrderedQueryable<TResult> source)
        => IsDescending ? source.ThenByDescending(keySelector) : source.ThenBy(keySelector);
    public IOrderedEnumerable<TResult> ApplyPrimary(IEnumerable<TResult> source)
        => IsDescending ? source.OrderByDescending(Compiled) : source.OrderBy(Compiled);
    public IOrderedEnumerable<TResult> ApplyThen(IOrderedEnumerable<TResult> source)
        => IsDescending ? source.ThenByDescending(Compiled) : source.ThenBy(Compiled);
    public string Describe()
        => $"{(IsThenBy ? "ThenBy" : "OrderBy")}{(IsDescending ? "Descending" : "")}<{typeof(TOrderKey).Name}>({keySelector})";
}

/// <summary>
/// Plan for Candidate C (single-call GroupBy(key, resultSelector), where Having/OrderBy operate on TResult).
/// </summary>
public sealed class ResultStageGroupPlan<T, TKey, TResult>(
    Expression<Func<T, TKey>> keySelector,
    Expression<Func<IGrouping<TKey, T>, TResult>> resultSelector) : IGroupByPlan<T, TResult>
{
    public Expression<Func<T, TKey>> KeySelector { get; } = keySelector;
    public Expression<Func<IGrouping<TKey, T>, TResult>> ResultSelector { get; } = resultSelector;
    public List<Expression<Func<TResult, bool>>> HavingPredicates { get; } = [];
    public List<IResultOrderStep<TResult>> OrderSteps { get; } = [];

    public Type KeyType => typeof(TKey);
    public bool HasResultSelector => true;
    public int? GroupSkip { get; set; }
    public int? GroupTake { get; set; }
    public bool IsPostGroupDistinct { get; set; }

    public IQueryable<TResult> Apply(IQueryable<T> criteriaSource)
    {
        var projected = criteriaSource.GroupBy(KeySelector).Select(ResultSelector);
        foreach (var h in HavingPredicates) projected = projected.Where(h);
        if (OrderSteps.Count > 0)
        {
            IOrderedQueryable<TResult>? ordered = null;
            foreach (var step in OrderSteps)
                ordered = ordered is null || !step.IsThenBy ? step.ApplyPrimary(projected) : step.ApplyThen(ordered);
            projected = ordered!;
        }
        if (IsPostGroupDistinct) projected = projected.Distinct();
        if (GroupSkip is int skip) projected = projected.Skip(skip);
        if (GroupTake is int take) projected = projected.Take(take);
        return projected;
    }

    public int CountGroups(IQueryable<T> criteriaSource)
    {
        var projected = criteriaSource.GroupBy(KeySelector).Select(ResultSelector);
        foreach (var h in HavingPredicates) projected = projected.Where(h);
        if (IsPostGroupDistinct) projected = projected.Distinct();
        return projected.Count();
    }

    public IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource)
    {
        var projected = criteriaSource.GroupBy(KeySelector.Compile()).Select(ResultSelector.Compile());
        foreach (var h in HavingPredicates) projected = projected.Where(h.Compile());
        if (OrderSteps.Count > 0)
        {
            IOrderedEnumerable<TResult>? ordered = null;
            foreach (var step in OrderSteps)
                ordered = ordered is null || !step.IsThenBy ? step.ApplyPrimary(projected) : step.ApplyThen(ordered);
            projected = ordered!;
        }
        if (IsPostGroupDistinct) projected = projected.Distinct();
        if (GroupSkip is int skip) projected = projected.Skip(skip);
        if (GroupTake is int take) projected = projected.Take(take);
        return projected;
    }

    public string Describe()
    {
        var parts = new List<string> { $"GroupBy<{typeof(TKey).Name}>({KeySelector}, {ResultSelector})" };
        parts.AddRange(HavingPredicates.Select(h => $"Having[TResult]({h})"));
        parts.AddRange(OrderSteps.Select(o => o.Describe()));
        if (IsPostGroupDistinct) parts.Add("Distinct()");
        if (GroupSkip is int s) parts.Add($"Skip({s})");
        if (GroupTake is int t) parts.Add($"Take({t})");
        return string.Join(" -> ", parts);
    }
}

/// <summary>
/// Prototype side-table standing in for an additive internal property on Specification&lt;T, TResult&gt;
/// (so the prototype runs without touching src/OpenSpecification).
/// </summary>
public static class PrototypePlanStore
{
    private static readonly ConditionalWeakTable<object, object> Plans = new();

    public static void Attach<T, TResult>(Specification<T, TResult> spec, IGroupByPlan<T, TResult> plan)
    {
        if (Plans.TryGetValue(spec, out _))
            throw new InvalidOperationException("GroupBy has already been configured on this specification.");
        Plans.Add(spec, plan);
    }

    public static IGroupByPlan<T, TResult>? Get<T, TResult>(ISpecification<T, TResult> spec)
        => Plans.TryGetValue(spec, out var plan) ? (IGroupByPlan<T, TResult>)plan : null;
}

public static class PrototypeEvaluator
{
    public static IEnumerable<TResult> Evaluate<T, TResult>(IEnumerable<T> source, ISpecification<T, TResult> spec)
    {
        var plan = PrototypePlanStore.Get(spec);
        if (plan is null)
            return InMemorySpecificationEvaluator.Default.Evaluate(source, spec);

        if (spec.Selector is not null || spec.SelectorMany is not null)
            throw new ConcurrentSelectorsException();

        // Criteria-only pre-group filtering (Where + Search, never entity OrderBy/Skip/Take)
        var filtered = ApplyCriteriaOnly(source, spec);
        // Also verify IQueryable<T> expression tree builds cleanly (like EF Core / EF6)
        _ = plan.Apply(filtered.AsQueryable()).Expression;

        var results = plan.Apply(filtered);
        return spec.PostProcessingAction is null ? results : spec.PostProcessingAction(results);
    }

    public static (int TotalGroups, List<TResult> Items) EvaluatePaged<T, TResult>(
        IEnumerable<T> source,
        ISpecification<T, TResult> spec,
        int page,
        int pageSize)
    {
        var plan = PrototypePlanStore.Get(spec)
            ?? throw new InvalidOperationException("Expected grouped specification.");

        var filtered = ApplyCriteriaOnly(source, spec).AsQueryable();
        var total = plan.CountGroups(filtered);
        var items = plan.Apply(filtered).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (total, items);
    }

    private static IEnumerable<T> ApplyCriteriaOnly<T>(IEnumerable<T> source, ISpecification<T> spec)
    {
        foreach (var where in spec.WhereExpressions)
            source = source.Where(where.FilterFunc);
        return source;
    }
}

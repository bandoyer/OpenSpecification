using System.Linq.Expressions;
using OpenSpecification;

namespace GroupByApiPrototype.CandidateA;

/// <summary>
/// CANDIDATE A: Fluent Staged Builder (isolated from ISpecificationBuilder&lt;T, TResult&gt;).
///
/// Query.Where(...)
///      .GroupBy(s => new { s.CompanyId, s.City })
///      .Having(g => g.Count() >= 2, highVolumeOnly)
///      .OrderByDescending(g => g.Sum(s => s.Amount), sortByRevenue)
///      .ThenBy(g => g.Key.City)
///      .Skip(0)
///      .Take(10)
///      .Select(g => new CitySalesDto { ... });
///
/// Key properties:
/// - Does NOT inherit ISpecificationBuilder&lt;T, TResult&gt;: IntelliSense after .GroupBy(...) shows ONLY valid group clauses.
/// - TKey is inferred on .GroupBy(...) and never leaks onto Specification&lt;T, TResult&gt;.
/// - Select(...) returns IGroupedResultSpecificationBuilder&lt;T, TResult&gt; so post-group Distinct / Skip / Take / PostProcessingAction / Cache chain cleanly without re-exposing entity clauses.
/// - Also tests an optional 2-arg shorthand GroupBy(keySelector, resultSelector) (Hybrid A+).
/// </summary>
public interface IGroupedSpecificationBuilder<T, TKey, TResult>
{
    Specification<T, TResult> Specification { get; }
}

public interface IOrderedGroupedSpecificationBuilder<T, TKey, TResult>
    : IGroupedSpecificationBuilder<T, TKey, TResult>
{
}

public interface IGroupedResultSpecificationBuilder<T, TResult>
{
    Specification<T, TResult> Specification { get; }
}

internal class GroupedSpecificationBuilder<T, TKey, TResult>(
    Specification<T, TResult> specification,
    GroupingStagePlan<T, TKey, TResult> plan,
    bool isOrderChainDiscarded = false)
    : IOrderedGroupedSpecificationBuilder<T, TKey, TResult>
{
    public Specification<T, TResult> Specification { get; } = specification;
    internal GroupingStagePlan<T, TKey, TResult> Plan { get; } = plan;
    internal bool IsOrderChainDiscarded { get; } = isOrderChainDiscarded;
}

internal sealed class GroupedResultSpecificationBuilder<T, TResult>(
    Specification<T, TResult> specification,
    IGroupByPlan<T, TResult> plan)
    : IGroupedResultSpecificationBuilder<T, TResult>
{
    public Specification<T, TResult> Specification { get; } = specification;
    internal IGroupByPlan<T, TResult> Plan { get; } = plan;
}

public static class GroupedBuilderExtensionsA
{
    // Entry point on ISpecificationBuilder<T, TResult> ONLY (not ISpecificationBuilder<T>)
    public static IGroupedSpecificationBuilder<T, TKey, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector)
    {
        var plan = new GroupingStagePlan<T, TKey, TResult>(keySelector);
        PrototypePlanStore.Attach(builder.Specification, plan);
        return new GroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan);
    }

    // Optional Hybrid A+ 2-arg shorthand (different arity -> zero overload ambiguity!)
    public static IGroupedResultSpecificationBuilder<T, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, T>, TResult>> resultSelector)
    {
        return builder.GroupBy(keySelector).Select(resultSelector);
    }

    public static IGroupedSpecificationBuilder<T, TKey, TResult> Having<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, bool>> predicate)
        => Having(builder, predicate, true);

    public static IGroupedSpecificationBuilder<T, TKey, TResult> Having<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, bool>> predicate,
        bool condition)
    {
        var concrete = (GroupedSpecificationBuilder<T, TKey, TResult>)builder;
        if (condition)
            concrete.Plan.HavingPredicates.Add(predicate);
        return concrete;
    }

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderBy<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => OrderBy(builder, keySelector, true);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderBy<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, isDescending: false, condition);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderByDescending<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => OrderByDescending(builder, keySelector, true);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderByDescending<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, isDescending: true, condition);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenBy<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => ThenBy(builder, keySelector, true);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenBy<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, isDescending: false, condition);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenByDescending<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => ThenByDescending(builder, keySelector, true);

    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenByDescending<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, isDescending: true, condition);

    public static IGroupedSpecificationBuilder<T, TKey, TResult> Skip<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int skip,
        bool condition = true)
    {
        if (condition)
        {
            var plan = ((GroupedSpecificationBuilder<T, TKey, TResult>)builder).Plan;
            if (plan.GroupSkip is not null) throw new DuplicateSkipException();
            plan.GroupSkip = skip;
        }
        return builder;
    }

    public static IGroupedSpecificationBuilder<T, TKey, TResult> Take<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int take,
        bool condition = true)
    {
        if (condition)
        {
            var plan = ((GroupedSpecificationBuilder<T, TKey, TResult>)builder).Plan;
            if (plan.GroupTake is not null) throw new DuplicateTakeException();
            plan.GroupTake = take;
        }
        return builder;
    }

    public static IGroupedResultSpecificationBuilder<T, TResult> Select<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TResult>> selector)
    {
        var concrete = (GroupedSpecificationBuilder<T, TKey, TResult>)builder;
        concrete.Plan.ResultSelector = selector;
        return new GroupedResultSpecificationBuilder<T, TResult>(concrete.Specification, concrete.Plan);
    }

    // Post-projection stage operations on IGroupedResultSpecificationBuilder<T, TResult>
    public static IGroupedResultSpecificationBuilder<T, TResult> Distinct<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        bool condition = true)
    {
        if (condition)
            ((GroupedResultSpecificationBuilder<T, TResult>)builder).Plan.IsPostGroupDistinct = true;
        return builder;
    }

    public static IGroupedResultSpecificationBuilder<T, TResult> Skip<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int skip,
        bool condition = true)
    {
        if (condition)
        {
            var plan = ((GroupedResultSpecificationBuilder<T, TResult>)builder).Plan;
            if (plan.GroupSkip is not null) throw new DuplicateSkipException();
            plan.GroupSkip = skip;
        }
        return builder;
    }

    public static IGroupedResultSpecificationBuilder<T, TResult> Take<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int take,
        bool condition = true)
    {
        if (condition)
        {
            var plan = ((GroupedResultSpecificationBuilder<T, TResult>)builder).Plan;
            if (plan.GroupTake is not null) throw new DuplicateTakeException();
            plan.GroupTake = take;
        }
        return builder;
    }

    public static IGroupedResultSpecificationBuilder<T, TResult> PostProcessingAction<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        Func<IEnumerable<TResult>, IEnumerable<TResult>> action)
    {
        builder.Specification.Query.PostProcessingAction(action);
        return builder;
    }

    private static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> AddOrderStart<T, TKey, TResult, TOrderKey>(
        IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool isDescending,
        bool condition)
    {
        var concrete = (GroupedSpecificationBuilder<T, TKey, TResult>)builder;
        if (condition)
        {
            concrete.Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, isDescending, isThenBy: false));
            return concrete.IsOrderChainDiscarded
                ? new GroupedSpecificationBuilder<T, TKey, TResult>(concrete.Specification, concrete.Plan, isOrderChainDiscarded: false)
                : concrete;
        }
        return new GroupedSpecificationBuilder<T, TKey, TResult>(concrete.Specification, concrete.Plan, isOrderChainDiscarded: true);
    }

    private static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> AddOrderContinuation<T, TKey, TResult, TOrderKey>(
        IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool isDescending,
        bool condition)
    {
        var concrete = (GroupedSpecificationBuilder<T, TKey, TResult>)builder;
        if (condition && !concrete.IsOrderChainDiscarded)
        {
            concrete.Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, isDescending, isThenBy: true));
            return concrete;
        }
        return concrete.IsOrderChainDiscarded
            ? concrete
            : new GroupedSpecificationBuilder<T, TKey, TResult>(concrete.Specification, concrete.Plan, isOrderChainDiscarded: true);
    }
}

// ---------- REAL CALL SITES (CANDIDATE A) ----------

public sealed class A1_SingleKeySpec : Specification<Store, CitySalesDto>
{
    public A1_SingleKeySpec()
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(s => s.City)
            .Select(g => new CitySalesDto
            {
                City = g.Key,
                StoreCount = g.Count(),
                TotalAmount = g.Sum(s => s.Amount),
                MaxAmount = g.Max(s => s.Amount),
            });
    }
}

public sealed class A1b_ShorthandSingleKeySpec : Specification<Store, CitySalesDto>
{
    public A1b_ShorthandSingleKeySpec()
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(
                s => s.City,
                g => new CitySalesDto
                {
                    City = g.Key,
                    StoreCount = g.Count(),
                    TotalAmount = g.Sum(s => s.Amount),
                    MaxAmount = g.Max(s => s.Amount),
                });
    }
}

public sealed class A2_CompositeKeyHavingOrderPageSpec : Specification<Store, CitySalesDto>
{
    public A2_CompositeKeyHavingOrderPageSpec(bool highVolumeOnly, bool sortByRevenue, int? skip = null, int? take = null)
    {
        Query
            .Where(s => s.IsActive)
            .AsNoTracking()
            .TagWith("sales-by-company-city")
            .GroupBy(s => new { s.CompanyId, s.City })
            .Having(g => g.Count() >= 2, highVolumeOnly)
            .OrderByDescending(g => g.Sum(s => s.Amount), sortByRevenue)
            .ThenBy(g => g.Key.City) // discarded if sortByRevenue == false!
            .OrderBy(g => g.Key.City, !sortByRevenue)
            .Skip(skip ?? 0, skip.HasValue)
            .Take(take ?? 0, take.HasValue)
            .Select(g => new CitySalesDto
            {
                CompanyId = g.Key.CompanyId,
                City = g.Key.City,
                StoreCount = g.Count(),
                TotalAmount = g.Sum(s => s.Amount),
                MaxAmount = g.Max(s => s.Amount),
            });
    }
}

public sealed class A5_UnprojectedAggregateScalarSpec : Specification<Store, string>
{
    public A5_UnprojectedAggregateScalarSpec()
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(s => s.City)
            .Having(g => g.Count() >= 2)
            .OrderByDescending(g => g.Sum(s => s.Amount))
            .Select(g => g.Key)
            .Distinct();
    }
}

public sealed class A7_SplitStatementDynamicSpec : Specification<Store, CitySalesDto>
{
    public A7_SplitStatementDynamicSpec(int? minStores, bool sortByTotal)
    {
        var group = Query
            .Where(s => s.IsActive)
            .GroupBy(s => new { s.CompanyId, s.City });

        if (minStores.HasValue)
            group.Having(g => g.Count() >= minStores.Value);

        if (sortByTotal)
            group.OrderByDescending(g => g.Sum(s => s.Amount)).ThenBy(g => g.Key.City);
        else
            group.OrderBy(g => g.Key.City);

        group.Select(g => new CitySalesDto
        {
            CompanyId = g.Key.CompanyId,
            City = g.Key.City,
            StoreCount = g.Count(),
            TotalAmount = g.Sum(s => s.Amount),
            MaxAmount = g.Max(s => s.Amount),
        })
        .Take(5);
    }
}

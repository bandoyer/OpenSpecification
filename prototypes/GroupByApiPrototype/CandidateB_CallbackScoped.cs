using System.Linq.Expressions;
using OpenSpecification;

namespace GroupByApiPrototype.CandidateB;

/// <summary>
/// CANDIDATE B: Callback-Scoped Builder with Compile-Time Mandatory Select().
///
/// Query.Where(...)
///      .GroupBy(
///          s => new { s.CompanyId, s.City },
///          group => group
///              .Having(g => g.Count() >= 2, highVolumeOnly)
///              .OrderByDescending(g => g.Sum(s => s.Amount), sortByRevenue)
///              .ThenBy(g => g.Key.City)
///              .Select(g => new CitySalesDto { ... }));
///
/// Key properties:
/// - Forgetting .Select(...) is a COMPILE-TIME error because the callback MUST return IGroupedProjection&lt;T, TKey, TResult&gt;.
/// - TKey is inferred from arg 1 and flows into arg 2's lambda parameter.
/// - Tradeoff: nested lambda syntax (group => group.Having(g => ...).Select(g => ...)) and two-level parameter names.
/// </summary>
public interface IGroupedProjection<T, TKey, TResult> { }

public interface IScopedGroupBuilder<T, TKey, TResult> { }

public interface IOrderedScopedGroupBuilder<T, TKey, TResult>
    : IScopedGroupBuilder<T, TKey, TResult> { }

public interface IPostGroupBuilderB<T, TResult>
{
    Specification<T, TResult> Specification { get; }
}

internal sealed class GroupedProjectionToken<T, TKey, TResult>(GroupingStagePlan<T, TKey, TResult> plan)
    : IGroupedProjection<T, TKey, TResult>
{
    internal GroupingStagePlan<T, TKey, TResult> Plan { get; } = plan;
}

internal sealed class ScopedGroupBuilder<T, TKey, TResult>(
    GroupingStagePlan<T, TKey, TResult> plan,
    bool isOrderChainDiscarded = false)
    : IOrderedScopedGroupBuilder<T, TKey, TResult>
{
    internal GroupingStagePlan<T, TKey, TResult> Plan { get; } = plan;
    internal bool IsOrderChainDiscarded { get; } = isOrderChainDiscarded;
}

internal sealed class PostGroupBuilderB<T, TResult>(
    Specification<T, TResult> spec,
    IGroupByPlan<T, TResult> plan) : IPostGroupBuilderB<T, TResult>
{
    public Specification<T, TResult> Specification { get; } = spec;
    internal IGroupByPlan<T, TResult> Plan { get; } = plan;
}

public static class GroupedBuilderExtensionsB
{
    public static IPostGroupBuilderB<T, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector,
        Func<IScopedGroupBuilder<T, TKey, TResult>, IGroupedProjection<T, TKey, TResult>> configure)
    {
        var plan = new GroupingStagePlan<T, TKey, TResult>(keySelector);
        var scope = new ScopedGroupBuilder<T, TKey, TResult>(plan);
        _ = configure(scope);
        PrototypePlanStore.Attach(builder.Specification, plan);
        return new PostGroupBuilderB<T, TResult>(builder.Specification, plan);
    }

    // Check whether adding a 2-arg direct selector overload alongside the callback overload causes ambiguity!
    // Enabled via #if CANDIDATE_B_WITH_DIRECT_OVERLOAD for negative/positive compiler check.
#if CANDIDATE_B_WITH_DIRECT_OVERLOAD
    public static IPostGroupBuilderB<T, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, T>, TResult>> resultSelector)
    {
        var plan = new GroupingStagePlan<T, TKey, TResult>(keySelector) { ResultSelector = resultSelector };
        PrototypePlanStore.Attach(builder.Specification, plan);
        return new PostGroupBuilderB<T, TResult>(builder.Specification, plan);
    }
#endif

    public static IScopedGroupBuilder<T, TKey, TResult> Having<T, TKey, TResult>(
        this IScopedGroupBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, bool>> predicate,
        bool condition = true)
    {
        var concrete = (ScopedGroupBuilder<T, TKey, TResult>)builder;
        if (condition) concrete.Plan.HavingPredicates.Add(predicate);
        return concrete;
    }

    public static IOrderedScopedGroupBuilder<T, TKey, TResult> OrderBy<T, TKey, TResult, TOrderKey>(
        this IScopedGroupBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ScopedGroupBuilder<T, TKey, TResult>)builder;
        if (condition)
        {
            concrete.Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, false, false));
            return concrete.IsOrderChainDiscarded
                ? new ScopedGroupBuilder<T, TKey, TResult>(concrete.Plan, false)
                : concrete;
        }
        return new ScopedGroupBuilder<T, TKey, TResult>(concrete.Plan, true);
    }

    public static IOrderedScopedGroupBuilder<T, TKey, TResult> OrderByDescending<T, TKey, TResult, TOrderKey>(
        this IScopedGroupBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ScopedGroupBuilder<T, TKey, TResult>)builder;
        if (condition)
        {
            concrete.Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, true, false));
            return concrete.IsOrderChainDiscarded
                ? new ScopedGroupBuilder<T, TKey, TResult>(concrete.Plan, false)
                : concrete;
        }
        return new ScopedGroupBuilder<T, TKey, TResult>(concrete.Plan, true);
    }

    public static IOrderedScopedGroupBuilder<T, TKey, TResult> ThenBy<T, TKey, TResult, TOrderKey>(
        this IOrderedScopedGroupBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ScopedGroupBuilder<T, TKey, TResult>)builder;
        if (condition && !concrete.IsOrderChainDiscarded)
        {
            concrete.Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, false, true));
            return concrete;
        }
        return concrete.IsOrderChainDiscarded ? concrete : new ScopedGroupBuilder<T, TKey, TResult>(concrete.Plan, true);
    }

    public static IGroupedProjection<T, TKey, TResult> Select<T, TKey, TResult>(
        this IScopedGroupBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TResult>> selector)
    {
        var concrete = (ScopedGroupBuilder<T, TKey, TResult>)builder;
        concrete.Plan.ResultSelector = selector;
        return new GroupedProjectionToken<T, TKey, TResult>(concrete.Plan);
    }

    public static IPostGroupBuilderB<T, TResult> Skip<T, TResult>(
        this IPostGroupBuilderB<T, TResult> builder,
        int skip,
        bool condition = true)
    {
        if (condition) ((PostGroupBuilderB<T, TResult>)builder).Plan.GroupSkip = skip;
        return builder;
    }

    public static IPostGroupBuilderB<T, TResult> Take<T, TResult>(
        this IPostGroupBuilderB<T, TResult> builder,
        int take,
        bool condition = true)
    {
        if (condition) ((PostGroupBuilderB<T, TResult>)builder).Plan.GroupTake = take;
        return builder;
    }

    public static IPostGroupBuilderB<T, TResult> Distinct<T, TResult>(
        this IPostGroupBuilderB<T, TResult> builder,
        bool condition = true)
    {
        if (condition) ((PostGroupBuilderB<T, TResult>)builder).Plan.IsPostGroupDistinct = true;
        return builder;
    }
}

// ---------- REAL CALL SITES (CANDIDATE B) ----------

public sealed class B1_SingleKeySpec : Specification<Store, CitySalesDto>
{
    public B1_SingleKeySpec()
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(
                s => s.City,
                group => group.Select(g => new CitySalesDto
                {
                    City = g.Key,
                    StoreCount = g.Count(),
                    TotalAmount = g.Sum(s => s.Amount),
                    MaxAmount = g.Max(s => s.Amount),
                }));
    }
}

public sealed class B2_CompositeKeyHavingOrderPageSpec : Specification<Store, CitySalesDto>
{
    public B2_CompositeKeyHavingOrderPageSpec(bool highVolumeOnly, bool sortByRevenue, int? skip = null, int? take = null)
    {
        Query
            .Where(s => s.IsActive)
            .AsNoTracking()
            .TagWith("sales-by-company-city")
            .GroupBy(
                s => new { s.CompanyId, s.City },
                group => group
                    .Having(g => g.Count() >= 2, highVolumeOnly)
                    .OrderByDescending(g => g.Sum(s => s.Amount), sortByRevenue)
                    .ThenBy(g => g.Key.City)
                    .OrderBy(g => g.Key.City, !sortByRevenue)
                    .Select(g => new CitySalesDto
                    {
                        CompanyId = g.Key.CompanyId,
                        City = g.Key.City,
                        StoreCount = g.Count(),
                        TotalAmount = g.Sum(s => s.Amount),
                        MaxAmount = g.Max(s => s.Amount),
                    }))
            .Skip(skip ?? 0, skip.HasValue)
            .Take(take ?? 0, take.HasValue);
    }
}

public sealed class B5_UnprojectedAggregateScalarSpec : Specification<Store, string>
{
    public B5_UnprojectedAggregateScalarSpec()
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(
                s => s.City,
                group => group
                    .Having(g => g.Count() >= 2)
                    .OrderByDescending(g => g.Sum(s => s.Amount))
                    .Select(g => g.Key))
            .Distinct();
    }
}

public sealed class B7_SplitStatementDynamicSpec : Specification<Store, CitySalesDto>
{
    public B7_SplitStatementDynamicSpec(int? minStores, bool sortByTotal)
    {
        Query
            .Where(s => s.IsActive)
            .GroupBy(
                s => new { s.CompanyId, s.City },
                group =>
                {
                    if (minStores.HasValue)
                        group.Having(g => g.Count() >= minStores.Value);

                    if (sortByTotal)
                        group.OrderByDescending(g => g.Sum(s => s.Amount)).ThenBy(g => g.Key.City);
                    else
                        group.OrderBy(g => g.Key.City);

                    return group.Select(g => new CitySalesDto
                    {
                        CompanyId = g.Key.CompanyId,
                        City = g.Key.City,
                        StoreCount = g.Count(),
                        TotalAmount = g.Sum(s => s.Amount),
                        MaxAmount = g.Max(s => s.Amount),
                    });
                })
            .Take(5);
    }
}

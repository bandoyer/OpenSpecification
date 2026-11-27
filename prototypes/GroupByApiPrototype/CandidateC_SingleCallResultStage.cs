using System.Linq.Expressions;
using OpenSpecification;

namespace GroupByApiPrototype.CandidateC;

/// <summary>
/// CANDIDATE C: Single-Call GroupBy(keySelector, resultSelector) + TResult-Stage Builder.
///
/// Query.Where(...)
///      .GroupBy(
///          s => new { s.CompanyId, s.City },
///          g => new CitySalesDto { ... })
///      .Having(r => r.StoreCount >= 2, highVolumeOnly)
///      .OrderByDescending(r => r.TotalAmount, sortByRevenue)
///      .ThenBy(r => r.City)
///      .Skip(0)
///      .Take(10);
///
/// Key properties:
/// - Compile-time guarantee of projection (resultSelector is required parameter of GroupBy).
/// - Cleanest syntax when every Having/OrderBy field is already projected onto TResult.
/// - MAJOR LIMITATION: Cannot express Having or OrderBy on UNPROJECTED aggregates (e.g., Specification&lt;Store, string&gt;
///   returning city names with Count >= 2 ordered by Sum(Amount) DESC)!
/// </summary>
public interface IGroupedResultBuilderC<T, TResult>
{
    Specification<T, TResult> Specification { get; }
}

public interface IOrderedGroupedResultBuilderC<T, TResult>
    : IGroupedResultBuilderC<T, TResult>
{
}

internal sealed class ResultStageBuilderC<T, TResult>(
    Specification<T, TResult> spec,
    Action<Expression<Func<TResult, bool>>> addHaving,
    Action<IResultOrderStep<TResult>> addOrder,
    IGroupByPlan<T, TResult> plan,
    bool isOrderChainDiscarded = false) : IOrderedGroupedResultBuilderC<T, TResult>
{
    public Specification<T, TResult> Specification { get; } = spec;
    internal Action<Expression<Func<TResult, bool>>> AddHaving { get; } = addHaving;
    internal Action<IResultOrderStep<TResult>> AddOrder { get; } = addOrder;
    internal IGroupByPlan<T, TResult> Plan { get; } = plan;
    internal bool IsOrderChainDiscarded { get; } = isOrderChainDiscarded;
}

public static class GroupedBuilderExtensionsC
{
    public static IGroupedResultBuilderC<T, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, T>, TResult>> resultSelector)
    {
        var plan = new ResultStageGroupPlan<T, TKey, TResult>(keySelector, resultSelector);
        PrototypePlanStore.Attach(builder.Specification, plan);
        return new ResultStageBuilderC<T, TResult>(
            builder.Specification,
            h => plan.HavingPredicates.Add(h),
            o => plan.OrderSteps.Add(o),
            plan);
    }

    public static IGroupedResultBuilderC<T, TResult> Having<T, TResult>(
        this IGroupedResultBuilderC<T, TResult> builder,
        Expression<Func<TResult, bool>> predicate,
        bool condition = true)
    {
        var concrete = (ResultStageBuilderC<T, TResult>)builder;
        if (condition) concrete.AddHaving(predicate);
        return concrete;
    }

    public static IOrderedGroupedResultBuilderC<T, TResult> OrderBy<T, TResult, TOrderKey>(
        this IGroupedResultBuilderC<T, TResult> builder,
        Expression<Func<TResult, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ResultStageBuilderC<T, TResult>)builder;
        if (condition)
        {
            concrete.AddOrder(new ResultOrderStep<TResult, TOrderKey>(keySelector, false, false));
            return concrete.IsOrderChainDiscarded
                ? new ResultStageBuilderC<T, TResult>(concrete.Specification, concrete.AddHaving, concrete.AddOrder, concrete.Plan, false)
                : concrete;
        }
        return new ResultStageBuilderC<T, TResult>(concrete.Specification, concrete.AddHaving, concrete.AddOrder, concrete.Plan, true);
    }

    public static IOrderedGroupedResultBuilderC<T, TResult> OrderByDescending<T, TResult, TOrderKey>(
        this IGroupedResultBuilderC<T, TResult> builder,
        Expression<Func<TResult, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ResultStageBuilderC<T, TResult>)builder;
        if (condition)
        {
            concrete.AddOrder(new ResultOrderStep<TResult, TOrderKey>(keySelector, true, false));
            return concrete.IsOrderChainDiscarded
                ? new ResultStageBuilderC<T, TResult>(concrete.Specification, concrete.AddHaving, concrete.AddOrder, concrete.Plan, false)
                : concrete;
        }
        return new ResultStageBuilderC<T, TResult>(concrete.Specification, concrete.AddHaving, concrete.AddOrder, concrete.Plan, true);
    }

    public static IOrderedGroupedResultBuilderC<T, TResult> ThenBy<T, TResult, TOrderKey>(
        this IOrderedGroupedResultBuilderC<T, TResult> builder,
        Expression<Func<TResult, TOrderKey>> keySelector,
        bool condition = true)
    {
        var concrete = (ResultStageBuilderC<T, TResult>)builder;
        if (condition && !concrete.IsOrderChainDiscarded)
        {
            concrete.AddOrder(new ResultOrderStep<TResult, TOrderKey>(keySelector, false, true));
            return concrete;
        }
        return concrete.IsOrderChainDiscarded
            ? concrete
            : new ResultStageBuilderC<T, TResult>(concrete.Specification, concrete.AddHaving, concrete.AddOrder, concrete.Plan, true);
    }

    public static IGroupedResultBuilderC<T, TResult> Skip<T, TResult>(
        this IGroupedResultBuilderC<T, TResult> builder,
        int skip,
        bool condition = true)
    {
        if (condition) ((ResultStageBuilderC<T, TResult>)builder).Plan.GroupSkip = skip;
        return builder;
    }

    public static IGroupedResultBuilderC<T, TResult> Take<T, TResult>(
        this IGroupedResultBuilderC<T, TResult> builder,
        int take,
        bool condition = true)
    {
        if (condition) ((ResultStageBuilderC<T, TResult>)builder).Plan.GroupTake = take;
        return builder;
    }

    public static IGroupedResultBuilderC<T, TResult> Distinct<T, TResult>(
        this IGroupedResultBuilderC<T, TResult> builder,
        bool condition = true)
    {
        if (condition) ((ResultStageBuilderC<T, TResult>)builder).Plan.IsPostGroupDistinct = true;
        return builder;
    }
}

// ---------- REAL CALL SITES (CANDIDATE C) ----------

public sealed class C1_SingleKeySpec : Specification<Store, CitySalesDto>
{
    public C1_SingleKeySpec()
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

public sealed class C2_CompositeKeyHavingOrderPageSpec : Specification<Store, CitySalesDto>
{
    public C2_CompositeKeyHavingOrderPageSpec(bool highVolumeOnly, bool sortByRevenue, int? skip = null, int? take = null)
    {
        Query
            .Where(s => s.IsActive)
            .AsNoTracking()
            .TagWith("sales-by-company-city")
            .GroupBy(
                s => new { s.CompanyId, s.City },
                g => new CitySalesDto
                {
                    CompanyId = g.Key.CompanyId,
                    City = g.Key.City,
                    StoreCount = g.Count(),
                    TotalAmount = g.Sum(s => s.Amount),
                    MaxAmount = g.Max(s => s.Amount),
                })
            .Having(r => r.StoreCount >= 2, highVolumeOnly)
            .OrderByDescending(r => r.TotalAmount, sortByRevenue)
            .ThenBy(r => r.City)
            .OrderBy(r => r.City, !sortByRevenue)
            .Skip(skip ?? 0, skip.HasValue)
            .Take(take ?? 0, take.HasValue);
    }
}

// Scenario 5 (Unprojected aggregate on Specification<Store, string>):
// IMPOSSIBLE IN CANDIDATE C! Once GroupBy(s => s.City, g => g.Key) returns IGroupedResultBuilderC<Store, string>,
// `g.Count()` and `g.Sum(s => s.Amount)` are no longer in scope; caller only has `string r`.

public sealed class C7_SplitStatementDynamicSpec : Specification<Store, CitySalesDto>
{
    public C7_SplitStatementDynamicSpec(int? minStores, bool sortByTotal)
    {
        // Note: builder type does NOT mention anonymous TKey, so it can even be passed to helper methods!
        IGroupedResultBuilderC<Store, CitySalesDto> grouped = Query
            .Where(s => s.IsActive)
            .GroupBy(
                s => new { s.CompanyId, s.City },
                g => new CitySalesDto
                {
                    CompanyId = g.Key.CompanyId,
                    City = g.Key.City,
                    StoreCount = g.Count(),
                    TotalAmount = g.Sum(s => s.Amount),
                    MaxAmount = g.Max(s => s.Amount),
                });

        if (minStores.HasValue)
            grouped.Having(r => r.StoreCount >= minStores.Value);

        if (sortByTotal)
            grouped.OrderByDescending(r => r.TotalAmount).ThenBy(r => r.City);
        else
            grouped.OrderBy(r => r.City);

        grouped.Take(5);
    }
}

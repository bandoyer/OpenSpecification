using System.Linq.Expressions;
using OpenSpecification;

namespace GroupByApiPrototype.CandidateD;

/// <summary>
/// CANDIDATE D: GroupedSpecification&lt;T, TKey, TResult&gt; Subclass.
///
/// public class SalesSpec : GroupedSpecification&lt;Store, CompanyCityKey, CitySalesDto&gt;
/// {
///     protected override Expression&lt;Func&lt;Store, CompanyCityKey&gt;&gt; GroupKey => ...;
///     protected override Expression&lt;Func&lt;IGrouping&lt;CompanyCityKey, Store&gt;, CitySalesDto&gt;&gt; GroupSelector => ...;
///     public SalesSpec() { Query.Where(...); GroupQuery.Having(...).OrderByDescending(...); }
/// }
///
/// Key properties:
/// - Still implements ISpecification&lt;T, TResult&gt; (inherits Specification&lt;T, TResult&gt;).
/// - Compile-time mandatory GroupKey + GroupSelector via abstract properties (impossible to forget Select!).
/// - MAJOR LIMITATION: Anonymous composite keys (`new { s.CompanyId, s.City }`) CANNOT be named as a class generic argument!
///   Callers must define a named key record/class (and for EF6, it CANNOT be a positional record or ValueTuple!).
/// </summary>
public abstract class GroupedSpecification<T, TKey, TResult> : Specification<T, TResult>
{
    private readonly GroupingStagePlan<T, TKey, TResult> _plan;

    protected GroupedSpecification()
    {
        // Note: reading virtual/abstract properties in base ctor is a C# hazard (CA2214) if derived ctor initializes fields first!
        // So either pass keySelector/resultSelector to base(...) or resolve lazily. Here we initialize lazily via GroupQuery.
        _plan = new GroupingStagePlan<T, TKey, TResult>(BuildKeySelector());
        _plan.ResultSelector = BuildGroupSelector();
        PrototypePlanStore.Attach(this, _plan);
    }

    protected abstract Expression<Func<T, TKey>> BuildKeySelector();
    protected abstract Expression<Func<IGrouping<TKey, T>, TResult>> BuildGroupSelector();

    protected GroupQueryBuilderD<T, TKey, TResult> GroupQuery => new(_plan);
}

public sealed class GroupQueryBuilderD<T, TKey, TResult>(
    GroupingStagePlan<T, TKey, TResult> plan,
    bool isOrderChainDiscarded = false)
{
    internal GroupingStagePlan<T, TKey, TResult> Plan { get; } = plan;
    internal bool IsOrderChainDiscarded { get; } = isOrderChainDiscarded;

    public GroupQueryBuilderD<T, TKey, TResult> Having(
        Expression<Func<IGrouping<TKey, T>, bool>> predicate,
        bool condition = true)
    {
        if (condition) Plan.HavingPredicates.Add(predicate);
        return this;
    }

    public OrderedGroupQueryBuilderD<T, TKey, TResult> OrderBy<TOrderKey>(
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        if (condition)
        {
            Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, false, false));
            return new OrderedGroupQueryBuilderD<T, TKey, TResult>(Plan, false);
        }
        return new OrderedGroupQueryBuilderD<T, TKey, TResult>(Plan, true);
    }

    public OrderedGroupQueryBuilderD<T, TKey, TResult> OrderByDescending<TOrderKey>(
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        if (condition)
        {
            Plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, true, false));
            return new OrderedGroupQueryBuilderD<T, TKey, TResult>(Plan, false);
        }
        return new OrderedGroupQueryBuilderD<T, TKey, TResult>(Plan, true);
    }

    public GroupQueryBuilderD<T, TKey, TResult> Skip(int skip, bool condition = true)
    {
        if (condition) Plan.GroupSkip = skip;
        return this;
    }

    public GroupQueryBuilderD<T, TKey, TResult> Take(int take, bool condition = true)
    {
        if (condition) Plan.GroupTake = take;
        return this;
    }

    public GroupQueryBuilderD<T, TKey, TResult> Distinct(bool condition = true)
    {
        if (condition) Plan.IsPostGroupDistinct = true;
        return this;
    }
}

public sealed class OrderedGroupQueryBuilderD<T, TKey, TResult>(
    GroupingStagePlan<T, TKey, TResult> plan,
    bool isOrderChainDiscarded)
{
    public OrderedGroupQueryBuilderD<T, TKey, TResult> ThenBy<TOrderKey>(
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
    {
        if (condition && !isOrderChainDiscarded)
        {
            plan.OrderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, false, true));
            return this;
        }
        return new OrderedGroupQueryBuilderD<T, TKey, TResult>(plan, true);
    }

    public OrderedGroupQueryBuilderD<T, TKey, TResult> OrderBy<TOrderKey>(
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition = true)
        => new GroupQueryBuilderD<T, TKey, TResult>(plan).OrderBy(keySelector, condition);

    public GroupQueryBuilderD<T, TKey, TResult> Skip(int skip, bool condition = true)
        => new GroupQueryBuilderD<T, TKey, TResult>(plan).Skip(skip, condition);

    public GroupQueryBuilderD<T, TKey, TResult> Take(int take, bool condition = true)
        => new GroupQueryBuilderD<T, TKey, TResult>(plan).Take(take, condition);
}

// ---------- REAL CALL SITES (CANDIDATE D) ----------

public sealed class D1_SingleKeySpec : GroupedSpecification<Store, string, CitySalesDto>
{
    public D1_SingleKeySpec()
    {
        Query.Where(s => s.IsActive);
    }

    protected override Expression<Func<Store, string>> BuildKeySelector() => s => s.City;

    protected override Expression<Func<IGrouping<string, Store>, CitySalesDto>> BuildGroupSelector() =>
        g => new CitySalesDto
        {
            City = g.Key,
            StoreCount = g.Count(),
            TotalAmount = g.Sum(s => s.Amount),
            MaxAmount = g.Max(s => s.Amount),
        };
}

// Composite key REQUIRES defining a named type `CompanyCityKey` (anonymous types impossible as class TKey!)
public sealed class D2_CompositeKeyHavingOrderPageSpec : GroupedSpecification<Store, CompanyCityKey, CitySalesDto>
{
    public D2_CompositeKeyHavingOrderPageSpec(bool highVolumeOnly, bool sortByRevenue, int? skip = null, int? take = null)
    {
        Query
            .Where(s => s.IsActive)
            .AsNoTracking()
            .TagWith("sales-by-company-city");

        GroupQuery
            .Having(g => g.Count() >= 2, highVolumeOnly)
            .OrderByDescending(g => g.Sum(s => s.Amount), sortByRevenue)
            .ThenBy(g => g.Key.City)
            .OrderBy(g => g.Key.City, !sortByRevenue)
            .Skip(skip ?? 0, skip.HasValue)
            .Take(take ?? 0, take.HasValue);
    }

    protected override Expression<Func<Store, CompanyCityKey>> BuildKeySelector()
        => s => new CompanyCityKey { CompanyId = s.CompanyId, City = s.City };

    protected override Expression<Func<IGrouping<CompanyCityKey, Store>, CitySalesDto>> BuildGroupSelector() =>
        g => new CitySalesDto
        {
            CompanyId = g.Key.CompanyId,
            City = g.Key.City,
            StoreCount = g.Count(),
            TotalAmount = g.Sum(s => s.Amount),
            MaxAmount = g.Max(s => s.Amount),
        };
}

public sealed class D5_UnprojectedAggregateScalarSpec : GroupedSpecification<Store, string, string>
{
    public D5_UnprojectedAggregateScalarSpec()
    {
        Query.Where(s => s.IsActive);
        GroupQuery
            .Having(g => g.Count() >= 2)
            .OrderByDescending(g => g.Sum(s => s.Amount));
        GroupQuery.Distinct();
    }

    protected override Expression<Func<Store, string>> BuildKeySelector() => s => s.City;
    protected override Expression<Func<IGrouping<string, Store>, string>> BuildGroupSelector() => g => g.Key;
}

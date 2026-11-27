namespace OpenSpecification;

internal interface IGroupByPlan<T, TResult>
{
    bool HasResultSelector { get; }
    IQueryable<TResult> Apply(IQueryable<T> criteriaSource);
    IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource);
    IGroupByPlan<T, TResult> Clone();
}

internal sealed class GroupHavingPredicate<TKey, T>
{
    private Func<IGrouping<TKey, T>, bool>? _compiledPredicate;

    public Expression<Func<IGrouping<TKey, T>, bool>> Predicate { get; }

    public Func<IGrouping<TKey, T>, bool> CompiledPredicate => _compiledPredicate ??= Predicate.Compile();

    public GroupHavingPredicate(Expression<Func<IGrouping<TKey, T>, bool>> predicate)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }
}

internal abstract class GroupOrderStep<T, TKey>
{
    public OrderTypeEnum OrderType { get; }

    public bool IsPrimary => OrderType is OrderTypeEnum.OrderBy or OrderTypeEnum.OrderByDescending;

    protected GroupOrderStep(OrderTypeEnum orderType)
    {
        OrderType = orderType;
    }

    public abstract IOrderedQueryable<IGrouping<TKey, T>> ApplyPrimary(IQueryable<IGrouping<TKey, T>> source);
    public abstract IOrderedQueryable<IGrouping<TKey, T>> ApplyThen(IOrderedQueryable<IGrouping<TKey, T>> source);
    public abstract IOrderedEnumerable<IGrouping<TKey, T>> ApplyPrimary(IEnumerable<IGrouping<TKey, T>> source);
    public abstract IOrderedEnumerable<IGrouping<TKey, T>> ApplyThen(IOrderedEnumerable<IGrouping<TKey, T>> source);
}

internal sealed class GroupOrderStep<T, TKey, TOrderKey> : GroupOrderStep<T, TKey>
{
    private Func<IGrouping<TKey, T>, TOrderKey>? _compiledKeySelector;

    public Expression<Func<IGrouping<TKey, T>, TOrderKey>> KeySelector { get; }

    private Func<IGrouping<TKey, T>, TOrderKey> CompiledKeySelector => _compiledKeySelector ??= KeySelector.Compile();

    public GroupOrderStep(Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector, OrderTypeEnum orderType)
        : base(orderType)
    {
        KeySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
    }

    public override IOrderedQueryable<IGrouping<TKey, T>> ApplyPrimary(IQueryable<IGrouping<TKey, T>> source)
        => OrderType == OrderTypeEnum.OrderByDescending
            ? source.OrderByDescending(KeySelector)
            : source.OrderBy(KeySelector);

    public override IOrderedQueryable<IGrouping<TKey, T>> ApplyThen(IOrderedQueryable<IGrouping<TKey, T>> source)
        => OrderType == OrderTypeEnum.ThenByDescending
            ? source.ThenByDescending(KeySelector)
            : source.ThenBy(KeySelector);

    public override IOrderedEnumerable<IGrouping<TKey, T>> ApplyPrimary(IEnumerable<IGrouping<TKey, T>> source)
        => OrderType == OrderTypeEnum.OrderByDescending
            ? source.OrderByDescending(CompiledKeySelector)
            : source.OrderBy(CompiledKeySelector);

    public override IOrderedEnumerable<IGrouping<TKey, T>> ApplyThen(IOrderedEnumerable<IGrouping<TKey, T>> source)
        => OrderType == OrderTypeEnum.ThenByDescending
            ? source.ThenByDescending(CompiledKeySelector)
            : source.ThenBy(CompiledKeySelector);
}

internal sealed class GroupingStagePlan<T, TKey, TResult> : IGroupByPlan<T, TResult>
{
    private Func<T, TKey>? _compiledKeySelector;
    private Func<IGrouping<TKey, T>, TResult>? _compiledResultSelector;
    private Expression<Func<IGrouping<TKey, T>, TResult>>? _resultSelector;
    private OneOrMany<GroupHavingPredicate<TKey, T>> _havingPredicates = new();
    private OneOrMany<GroupOrderStep<T, TKey>> _orderSteps = new();

    public Expression<Func<T, TKey>> KeySelector { get; }

    public Expression<Func<IGrouping<TKey, T>, TResult>>? ResultSelector
    {
        get => _resultSelector;
        set
        {
            _resultSelector = value;
            _compiledResultSelector = null;
        }
    }

    public bool HasResultSelector => _resultSelector is not null;

    public GroupingStagePlan(Expression<Func<T, TKey>> keySelector)
    {
        KeySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
    }

    private GroupingStagePlan(
        Expression<Func<T, TKey>> keySelector,
        Func<T, TKey>? compiledKeySelector,
        Expression<Func<IGrouping<TKey, T>, TResult>>? resultSelector,
        Func<IGrouping<TKey, T>, TResult>? compiledResultSelector,
        OneOrMany<GroupHavingPredicate<TKey, T>> havingPredicates,
        OneOrMany<GroupOrderStep<T, TKey>> orderSteps)
    {
        KeySelector = keySelector;
        _compiledKeySelector = compiledKeySelector;
        _resultSelector = resultSelector;
        _compiledResultSelector = compiledResultSelector;
        _havingPredicates = havingPredicates;
        _orderSteps = orderSteps;
    }

    public void AddHaving(Expression<Func<IGrouping<TKey, T>, bool>> predicate)
        => _havingPredicates.Add(new GroupHavingPredicate<TKey, T>(predicate));

    public void AddOrderStep<TOrderKey>(Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector, OrderTypeEnum orderType)
        => _orderSteps.Add(new GroupOrderStep<T, TKey, TOrderKey>(keySelector, orderType));

    public IQueryable<TResult> Apply(IQueryable<T> criteriaSource)
    {
        var resultSelector = _resultSelector ?? throw new SelectorNotFoundException();
        var grouped = criteriaSource.GroupBy(KeySelector);

        if (_havingPredicates.SingleOrDefault is { } singleHaving)
        {
            grouped = grouped.Where(singleHaving.Predicate);
        }
        else if (!_havingPredicates.IsEmpty)
        {
            foreach (var having in _havingPredicates.List)
            {
                grouped = grouped.Where(having.Predicate);
            }
        }

        if (_orderSteps.SingleOrDefault is { } singleOrder)
        {
            if (singleOrder.IsPrimary)
            {
                grouped = singleOrder.ApplyPrimary(grouped);
            }
        }
        else if (!_orderSteps.IsEmpty)
        {
            IOrderedQueryable<IGrouping<TKey, T>>? ordered = null;
            var chainCount = 0;
            foreach (var step in _orderSteps.List)
            {
                if (step.IsPrimary)
                {
                    chainCount++;
                    if (chainCount == 2) throw new DuplicateOrderChainException();
                    ordered = step.ApplyPrimary(grouped);
                }
                else if (ordered is not null)
                {
                    ordered = step.ApplyThen(ordered);
                }
            }

            if (ordered is not null)
            {
                grouped = ordered;
            }
        }

        return grouped.Select(resultSelector);
    }

    public IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource)
    {
        var resultSelector = _resultSelector ?? throw new SelectorNotFoundException();
        var keyFunc = _compiledKeySelector ??= KeySelector.Compile();
        var resultFunc = _compiledResultSelector ??= resultSelector.Compile();

        var grouped = criteriaSource.GroupBy(keyFunc, EqualityComparer<TKey>.Default);

        if (_havingPredicates.SingleOrDefault is { } singleHaving)
        {
            grouped = grouped.Where(singleHaving.CompiledPredicate);
        }
        else if (!_havingPredicates.IsEmpty)
        {
            foreach (var having in _havingPredicates.List)
            {
                grouped = grouped.Where(having.CompiledPredicate);
            }
        }

        if (_orderSteps.SingleOrDefault is { } singleOrder)
        {
            if (singleOrder.IsPrimary)
            {
                grouped = singleOrder.ApplyPrimary(grouped);
            }
        }
        else if (!_orderSteps.IsEmpty)
        {
            IOrderedEnumerable<IGrouping<TKey, T>>? ordered = null;
            var chainCount = 0;
            foreach (var step in _orderSteps.List)
            {
                if (step.IsPrimary)
                {
                    chainCount++;
                    if (chainCount == 2) throw new DuplicateOrderChainException();
                    ordered = step.ApplyPrimary(grouped);
                }
                else if (ordered is not null)
                {
                    ordered = step.ApplyThen(ordered);
                }
            }

            if (ordered is not null)
            {
                grouped = ordered;
            }
        }

        return grouped.Select(resultFunc);
    }

    public IGroupByPlan<T, TResult> Clone()
        => new GroupingStagePlan<T, TKey, TResult>(
            KeySelector,
            _compiledKeySelector,
            _resultSelector,
            _compiledResultSelector,
            _havingPredicates.Clone(),
            _orderSteps.Clone());
}

internal static class GroupByHelper
{
    internal static IGroupByPlan<T, TResult>? GetPlan<T, TResult>(ISpecification<T, TResult> specification)
        => specification is Specification<T, TResult> spec ? spec.GroupByPlan : null;

    internal static void Validate<T, TResult>(ISpecification<T, TResult> specification, IGroupByPlan<T, TResult> plan)
    {
        if (specification.Selector is not null || specification.SelectorMany is not null)
            throw new ConcurrentSelectorsException();

        if (!plan.HasResultSelector)
            throw new SelectorNotFoundException();

        var hasOrdering = specification is Specification<T> spec
            ? !spec.OneOrManyOrderExpressions.IsEmpty
            : specification.OrderExpressions.Any();

        if (hasOrdering)
            throw new NotSupportedException("Ordering entities before GroupBy is not supported. Remove entity OrderBy from the specification.");

        if (specification.Skip != -1 || specification.Take != -1)
            throw new NotSupportedException("Applying entity Skip or Take before GroupBy is not supported. Remove entity Skip and Take from the specification.");

        if (DistinctEvaluator.IsDistinctSpecification(specification))
            throw new NotSupportedException("Combining Distinct with GroupBy is not yet supported.");
    }
}

namespace OpenSpecification;

internal interface IGroupByPlan<T, TResult>
{
    bool HasResultSelector { get; }
    IQueryable<TResult> Apply(IQueryable<T> criteriaSource);
    IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource);
    IGroupByPlan<T, TResult> Clone();
}

internal sealed class GroupingStagePlan<T, TKey, TResult> : IGroupByPlan<T, TResult>
{
    private Func<T, TKey>? _compiledKeySelector;
    private Func<IGrouping<TKey, T>, TResult>? _compiledResultSelector;
    private Expression<Func<IGrouping<TKey, T>, TResult>>? _resultSelector;

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
        Func<IGrouping<TKey, T>, TResult>? compiledResultSelector)
    {
        KeySelector = keySelector;
        _compiledKeySelector = compiledKeySelector;
        _resultSelector = resultSelector;
        _compiledResultSelector = compiledResultSelector;
    }

    public IQueryable<TResult> Apply(IQueryable<T> criteriaSource)
    {
        var resultSelector = _resultSelector ?? throw new SelectorNotFoundException();
        return criteriaSource.GroupBy(KeySelector).Select(resultSelector);
    }

    public IEnumerable<TResult> Apply(IEnumerable<T> criteriaSource)
    {
        var resultSelector = _resultSelector ?? throw new SelectorNotFoundException();
        var keyFunc = _compiledKeySelector ??= KeySelector.Compile();
        var resultFunc = _compiledResultSelector ??= resultSelector.Compile();

        return criteriaSource
            .GroupBy(keyFunc, EqualityComparer<TKey>.Default)
            .Select(resultFunc);
    }

    public IGroupByPlan<T, TResult> Clone()
        => new GroupingStagePlan<T, TKey, TResult>(
            KeySelector,
            _compiledKeySelector,
            _resultSelector,
            _compiledResultSelector);
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

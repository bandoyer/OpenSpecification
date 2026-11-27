namespace OpenSpecification.EntityFramework6;

/// <inheritdoc/>
public class SpecificationEvaluator : ISpecificationEvaluator
{
    // Will use singleton for default configuration. Yet, it can be instantiated if necessary, with default or provided evaluators.
    public static SpecificationEvaluator Default { get; } = new SpecificationEvaluator();

    protected List<IEvaluator> Evaluators { get; } = new List<IEvaluator>();

    public SpecificationEvaluator()
    {
        Evaluators.AddRange(new IEvaluator[]
        {
            WhereEvaluator.Instance,
            SearchEvaluator.Instance,
            DistinctEvaluator.Instance,
            IncludeStringEvaluator.Instance,
            IncludeEvaluator.Instance,
            OrderEvaluator.Instance,
            PaginationEvaluator.Instance,
            AsNoTrackingEvaluator.Instance
        });
    }
    public SpecificationEvaluator(IEnumerable<IEvaluator> evaluators)
    {
        Evaluators.AddRange(evaluators);
    }

    /// <inheritdoc/>
    public virtual IQueryable<TResult> GetQuery<T, TResult>(IQueryable<T> query, ISpecification<T, TResult> specification) where T : class
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));

        if (GroupByHelper.GetPlan(specification) is { } groupByPlan)
        {
            GroupByHelper.Validate(specification, groupByPlan);
            query = GetQuery(query, (ISpecification<T>)specification, evaluateCriteriaOnly: true);
            return groupByPlan.Apply(query, DistinctEvaluator.IsDistinctSpecification(specification));
        }

        if (specification.Selector is null && specification.SelectorMany is null) throw new SelectorNotFoundException();
        if (specification.Selector != null && specification.SelectorMany != null) throw new ConcurrentSelectorsException();

        if (DistinctEvaluator.IsDistinctSpecification(specification))
        {
            DistinctEvaluator.ThrowIfOrderedProjection(specification);
            query = GetQuery(query, (ISpecification<T>)specification, evaluateCriteriaOnly: true);
            return DistinctEvaluator.ApplyDistinctProjection(query, specification);
        }

        query = GetQuery(query, (ISpecification<T>)specification);

        return specification.Selector != null
          ? query.Select(specification.Selector)
          : query.SelectMany(specification.SelectorMany!);
    }

    /// <inheritdoc/>
    public virtual IQueryable<T> GetQuery<T>(IQueryable<T> query, ISpecification<T> specification, bool evaluateCriteriaOnly = false) where T : class
    {
        if (specification is null) throw new ArgumentNullException(nameof(specification));

        var evaluators = evaluateCriteriaOnly ? Evaluators.Where(x => x.IsCriteriaEvaluator) : Evaluators;

        foreach (var evaluator in evaluators)
        {
            query = evaluator.GetQuery(query, specification);
        }

        return query;
    }
}

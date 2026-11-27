using System.Collections.Concurrent;

namespace OpenSpecification;

/// <summary>
/// Represents an evaluator that applies <c>Distinct()</c> for entity specifications.
/// Projection specifications apply <c>Distinct()</c> after projection in the specification evaluator.
/// </summary>
public class DistinctEvaluator : IEvaluator, IInMemoryEvaluator
{
    private static readonly ConcurrentDictionary<Type, bool> _projectionTypes = new();

    /// <summary>
    /// Gets the singleton instance of the <see cref="DistinctEvaluator"/> class.
    /// </summary>
    public static DistinctEvaluator Instance { get; } = new DistinctEvaluator();
    private DistinctEvaluator() { }

    /// <inheritdoc/>
    public bool IsCriteriaEvaluator { get; } = true;

    /// <inheritdoc/>
    public IQueryable<T> GetQuery<T>(IQueryable<T> query, ISpecification<T> specification) where T : class
        => ShouldApplyEntityDistinct(specification) ? query.Distinct() : query;

    /// <inheritdoc/>
    public IEnumerable<T> Evaluate<T>(IEnumerable<T> query, ISpecification<T> specification)
        => ShouldApplyEntityDistinct(specification) ? query.Distinct() : query;

    internal static bool IsDistinctSpecification<T>(ISpecification<T> specification)
        => specification is IDistinctSpecification { IsDistinct: true };

    internal static void ThrowIfOrderedProjection<T>(ISpecification<T> specification)
    {
        var hasOrdering = specification is Specification<T> spec
            ? !spec.OneOrManyOrderExpressions.IsEmpty
            : specification.OrderExpressions.Any();

        if (hasOrdering)
        {
            throw new NotSupportedException("Ordering entities before a distinct projection is not supported. Remove OrderBy from the specification.");
        }
    }

    internal static IQueryable<TResult> ApplyDistinctProjection<T, TResult>(IQueryable<T> query, ISpecification<T, TResult> specification)
    {
        var projected = specification.Selector is not null
            ? query.Select(specification.Selector)
            : query.SelectMany(specification.SelectorMany!);

        projected = projected.Distinct();

        // If skip is 0, avoid adding to the IQueryable, as PaginationEvaluator does.
        if (specification.Skip > 0)
        {
            projected = projected.Skip(specification.Skip);
        }

        if (specification.Take >= 0)
        {
            projected = projected.Take(specification.Take);
        }

        return projected;
    }

    internal static IEnumerable<TResult> ApplyDistinctProjection<T, TResult>(IEnumerable<T> source, ISpecification<T, TResult> specification)
    {
        var projected = specification.Selector is not null
            ? source.Select(specification.Selector.Compile())
            : source.SelectMany(specification.SelectorMany!.Compile());

        projected = projected.Distinct();

        if (specification.Skip > 0)
        {
            projected = projected.Skip(specification.Skip);
        }

        if (specification.Take >= 0)
        {
            projected = projected.Take(specification.Take);
        }

        return projected;
    }

    private static bool ShouldApplyEntityDistinct<T>(ISpecification<T> specification)
    {
        if (specification is Specification<T> spec)
        {
            return spec.IsDistinct && !spec.IsProjection;
        }

        return specification is IDistinctSpecification { IsDistinct: true }
            && !_projectionTypes.GetOrAdd(specification.GetType(), static type =>
                type.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISpecification<,>)));
    }
}

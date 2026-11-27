namespace OpenSpecification;

/// <summary>
/// Represents a specification builder in the grouped stage after <c>GroupBy</c>.
/// </summary>
/// <typeparam name="T">The type of the entity.</typeparam>
/// <typeparam name="TKey">The type of the grouping key.</typeparam>
/// <typeparam name="TResult">The type of the projected result.</typeparam>
public interface IGroupedSpecificationBuilder<T, TKey, TResult>
{
    /// <summary>
    /// Gets the specification associated with this builder.
    /// </summary>
    Specification<T, TResult> Specification { get; }
}

/// <summary>
/// Represents a specification builder in the post-projection stage after grouped <c>Select</c>.
/// </summary>
/// <typeparam name="T">The type of the entity.</typeparam>
/// <typeparam name="TResult">The type of the projected result.</typeparam>
public interface IGroupedResultSpecificationBuilder<T, TResult>
{
    /// <summary>
    /// Gets the specification associated with this builder.
    /// </summary>
    Specification<T, TResult> Specification { get; }
}

internal sealed class GroupedSpecificationBuilder<T, TKey, TResult>
    : IGroupedSpecificationBuilder<T, TKey, TResult>
{
    public Specification<T, TResult> Specification { get; }
    internal GroupingStagePlan<T, TKey, TResult> Plan { get; }

    public GroupedSpecificationBuilder(
        Specification<T, TResult> specification,
        GroupingStagePlan<T, TKey, TResult> plan)
    {
        Specification = specification;
        Plan = plan;
    }
}

internal sealed class GroupedResultSpecificationBuilder<T, TResult>
    : IGroupedResultSpecificationBuilder<T, TResult>
{
    public Specification<T, TResult> Specification { get; }

    public GroupedResultSpecificationBuilder(Specification<T, TResult> specification)
    {
        Specification = specification;
    }
}

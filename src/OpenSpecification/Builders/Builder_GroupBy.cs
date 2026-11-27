namespace OpenSpecification;

public static partial class SpecificationBuilderExtensions
{
    /// <summary>
    /// Groups matching entities by <paramref name="keySelector"/> and transitions the builder to the grouped stage.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>A grouped specification builder for projecting each group into <typeparamref name="TResult"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateGroupByException">Thrown when <c>GroupBy</c> has already been configured on the specification.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> GroupBy<T, TKey, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, TKey>> keySelector)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));

        if (builder.Specification.GroupByPlan is not null)
        {
            throw new DuplicateGroupByException();
        }

        var plan = new GroupingStagePlan<T, TKey, TResult>(keySelector);
        builder.Specification.GroupByPlan = plan;
        return new GroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan);
    }

    /// <summary>
    /// Projects each entity group into <typeparamref name="TResult"/> and transitions the builder to the grouped result stage.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="selector">The group projection expression.</param>
    /// <returns>A grouped result specification builder for post-projection configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="selector"/> is <see langword="null"/>.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Select<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TResult>> selector)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (selector is null) throw new ArgumentNullException(nameof(selector));

        var plan = builder is GroupedSpecificationBuilder<T, TKey, TResult> concrete
            ? concrete.Plan
            : builder.Specification.GroupByPlan as GroupingStagePlan<T, TKey, TResult>
                ?? throw new InvalidOperationException("The grouped specification builder is not associated with a matching GroupBy plan.");

        plan.ResultSelector = selector;
        return new GroupedResultSpecificationBuilder<T, TResult>(builder.Specification);
    }

    /// <summary>
    /// Specifies an in-memory function to apply to the grouped projection results returned by list-returning methods.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="filter">The post-processing function.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    public static IGroupedResultSpecificationBuilder<T, TResult> PostProcessingAction<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        Func<IEnumerable<TResult>, IEnumerable<TResult>> filter)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        builder.Specification.PostProcessingAction = filter;
        return builder;
    }

    /// <summary>
    /// Sets the cache key for the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="specificationName">Used as prefix for the cache key.</param>
    /// <param name="args">To be appended to the cache key, separated by dash.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="specificationName"/> is null or empty.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> EnableCache<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        string specificationName,
        params object[] args)
        => EnableCache(builder, specificationName, true, args);

    /// <summary>
    /// Sets the cache key for the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="specificationName">Used as prefix for the cache key.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="args">To be appended to the cache key, separated by dash.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="specificationName"/> is null or empty and <paramref name="condition"/> is true.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> EnableCache<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        string specificationName,
        bool condition,
        params object[] args)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        if (condition)
        {
            if (string.IsNullOrEmpty(specificationName))
            {
                throw new ArgumentException("Required input was null or empty.", nameof(specificationName));
            }

            builder.Specification.CacheKey = $"{specificationName}-{string.Join("-", args)}";
        }

        return builder;
    }

    /// <summary>
    /// Sets the cache key for the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="cacheKey">The cache key to be used.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cacheKey"/> is null or empty.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> WithCacheKey<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        string cacheKey)
        => WithCacheKey(builder, cacheKey, true);

    /// <summary>
    /// Sets the cache key for the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="cacheKey">The cache key to be used.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cacheKey"/> is null or empty and <paramref name="condition"/> is true.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> WithCacheKey<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        string cacheKey,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        if (condition)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                throw new ArgumentException("Required input was null or empty.", nameof(cacheKey));
            }

            builder.Specification.CacheKey = cacheKey;
        }

        return builder;
    }
}

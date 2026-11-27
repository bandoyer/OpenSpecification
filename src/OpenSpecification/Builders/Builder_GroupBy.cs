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
    /// <returns>A grouped specification builder for filtering, ordering, and projecting each group into <typeparamref name="TResult"/>.</returns>
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
    /// Adds a group-level filter (<c>HAVING</c>) predicate to the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="predicate">The group predicate expression.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Having<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, bool>> predicate)
        => Having(builder, predicate, true);

    /// <summary>
    /// Adds a group-level filter (<c>HAVING</c>) predicate to the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="predicate">The group predicate expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when <paramref name="condition"/> is <see langword="true"/> and <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Having<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, bool>> predicate,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        var plan = GetGroupByPlan(builder);
        if (condition)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            plan.AddHaving(predicate);
        }

        return builder is OrderedGroupedSpecificationBuilder<T, TKey, TResult>
            ? new GroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan)
            : builder;
    }

    /// <summary>
    /// Adds an ascending group-level <c>OrderBy</c> clause to the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <returns>An ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderBy<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => OrderBy(builder, keySelector, true);

    /// <summary>
    /// Adds an ascending group-level <c>OrderBy</c> clause to the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>An ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when <paramref name="condition"/> is <see langword="true"/> and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderBy<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddGroupOrderStart(builder, keySelector, OrderTypeEnum.OrderBy, condition);

    /// <summary>
    /// Adds a descending group-level <c>OrderByDescending</c> clause to the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <returns>An ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderByDescending<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => OrderByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds a descending group-level <c>OrderByDescending</c> clause to the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>An ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when <paramref name="condition"/> is <see langword="true"/> and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> OrderByDescending<T, TKey, TResult, TOrderKey>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddGroupOrderStart(builder, keySelector, OrderTypeEnum.OrderByDescending, condition);

    /// <summary>
    /// Adds a subsequent ascending group-level <c>ThenBy</c> clause to the ordered grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The ordered grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <returns>The updated ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when the order chain is active and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenBy<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => ThenBy(builder, keySelector, true);

    /// <summary>
    /// Adds a subsequent ascending group-level <c>ThenBy</c> clause to the ordered grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The ordered grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when <paramref name="condition"/> is <see langword="true"/> on an active order chain and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenBy<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddGroupOrderContinuation(builder, keySelector, OrderTypeEnum.ThenBy, condition);

    /// <summary>
    /// Adds a subsequent descending group-level <c>ThenByDescending</c> clause to the ordered grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The ordered grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <returns>The updated ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when the order chain is active and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenByDescending<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector)
        => ThenByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds a subsequent descending group-level <c>ThenByDescending</c> clause to the ordered grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <typeparam name="TOrderKey">The type of the ordering key.</typeparam>
    /// <param name="builder">The ordered grouped specification builder.</param>
    /// <param name="keySelector">The group ordering key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>, or when <paramref name="condition"/> is <see langword="true"/> on an active order chain and <paramref name="keySelector"/> is <see langword="null"/>.</exception>
    public static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> ThenByDescending<T, TKey, TResult, TOrderKey>(
        this IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        bool condition)
        => AddGroupOrderContinuation(builder, keySelector, OrderTypeEnum.ThenByDescending, condition);

    /// <summary>
    /// Sets the number of groups to skip in the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="skip">The number of groups to skip.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateSkipException">Thrown when group-level <c>Skip</c> is already set.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Skip<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int skip)
        => Skip(builder, skip, true);

    /// <summary>
    /// Sets the number of groups to skip in the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="skip">The number of groups to skip.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateSkipException">Thrown when <paramref name="condition"/> is <see langword="true"/> and group-level <c>Skip</c> is already set.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Skip<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int skip,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        var plan = GetGroupByPlan(builder);
        SetGroupSkip(plan, skip, condition);

        return builder is OrderedGroupedSpecificationBuilder<T, TKey, TResult>
            ? new GroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan)
            : builder;
    }

    /// <summary>
    /// Sets the number of groups to take in the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="take">The number of groups to take.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateTakeException">Thrown when group-level <c>Take</c> is already set.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Take<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int take)
        => Take(builder, take, true);

    /// <summary>
    /// Sets the number of groups to take in the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped specification builder.</param>
    /// <param name="take">The number of groups to take.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The grouped specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateTakeException">Thrown when <paramref name="condition"/> is <see langword="true"/> and group-level <c>Take</c> is already set.</exception>
    public static IGroupedSpecificationBuilder<T, TKey, TResult> Take<T, TKey, TResult>(
        this IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        int take,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        var plan = GetGroupByPlan(builder);
        SetGroupTake(plan, take, condition);

        return builder is OrderedGroupedSpecificationBuilder<T, TKey, TResult>
            ? new GroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan)
            : builder;
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

        var plan = GetGroupByPlan(builder);
        plan.ResultSelector = selector;
        return new GroupedResultSpecificationBuilder<T, TResult>(builder.Specification);
    }

    private static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> AddGroupOrderStart<T, TKey, TResult, TOrderKey>(
        IGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        var plan = GetGroupByPlan(builder);
        if (condition)
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            plan.AddOrderStep(keySelector, orderType);
            return new OrderedGroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan, isChainDiscarded: false);
        }

        return new OrderedGroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan, isChainDiscarded: true);
    }

    private static IOrderedGroupedSpecificationBuilder<T, TKey, TResult> AddGroupOrderContinuation<T, TKey, TResult, TOrderKey>(
        IOrderedGroupedSpecificationBuilder<T, TKey, TResult> builder,
        Expression<Func<IGrouping<TKey, T>, TOrderKey>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        var plan = GetGroupByPlan(builder);
        var isDiscarded = builder is OrderedGroupedSpecificationBuilder<T, TKey, TResult> { IsChainDiscarded: true };
        if (condition && !isDiscarded)
        {
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            plan.AddOrderStep(keySelector, orderType);
            return builder;
        }

        return isDiscarded
            ? builder
            : new OrderedGroupedSpecificationBuilder<T, TKey, TResult>(builder.Specification, plan, isChainDiscarded: true);
    }

    private static GroupingStagePlan<T, TKey, TResult> GetGroupByPlan<T, TKey, TResult>(
        IGroupedSpecificationBuilder<T, TKey, TResult> builder)
        => builder is GroupedSpecificationBuilder<T, TKey, TResult> concrete
            ? concrete.Plan
            : builder.Specification.GroupByPlan as GroupingStagePlan<T, TKey, TResult>
                ?? throw new InvalidOperationException("The grouped specification builder is not associated with a matching GroupBy plan.");

    private static IGroupByPlan<T, TResult> GetGroupByPlan<T, TResult>(
        IGroupedResultSpecificationBuilder<T, TResult> builder)
        => builder.Specification.GroupByPlan
            ?? throw new InvalidOperationException("The grouped result specification builder is not associated with a GroupBy plan.");

    private static void SetGroupSkip<T, TResult>(IGroupByPlan<T, TResult> plan, int skip, bool condition)
    {
        if (condition)
        {
            plan.SetSkip(skip);
        }
    }

    private static void SetGroupTake<T, TResult>(IGroupByPlan<T, TResult> plan, int take, bool condition)
    {
        if (condition)
        {
            plan.SetTake(take);
        }
    }

    /// <summary>
    /// Configures the grouped specification to deduplicate projected group results.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Distinct<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder)
        => Distinct(builder, true);

    /// <summary>
    /// Configures the grouped specification to deduplicate projected group results when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Distinct<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        if (condition)
        {
            builder.Specification.IsDistinct = true;
        }

        return builder;
    }

    /// <summary>
    /// Sets the number of grouped results to skip in the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="skip">The number of grouped results to skip.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateSkipException">Thrown when group-level <c>Skip</c> is already set.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Skip<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int skip)
        => Skip(builder, skip, true);

    /// <summary>
    /// Sets the number of grouped results to skip in the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="skip">The number of grouped results to skip.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateSkipException">Thrown when <paramref name="condition"/> is <see langword="true"/> and group-level <c>Skip</c> is already set.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Skip<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int skip,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        SetGroupSkip(GetGroupByPlan(builder), skip, condition);
        return builder;
    }

    /// <summary>
    /// Sets the number of grouped results to take in the grouped specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="take">The number of grouped results to take.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateTakeException">Thrown when group-level <c>Take</c> is already set.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Take<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int take)
        => Take(builder, take, true);

    /// <summary>
    /// Sets the number of grouped results to take in the grouped specification when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the projected result.</typeparam>
    /// <param name="builder">The grouped result specification builder.</param>
    /// <param name="take">The number of grouped results to take.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated grouped result specification builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="DuplicateTakeException">Thrown when <paramref name="condition"/> is <see langword="true"/> and group-level <c>Take</c> is already set.</exception>
    public static IGroupedResultSpecificationBuilder<T, TResult> Take<T, TResult>(
        this IGroupedResultSpecificationBuilder<T, TResult> builder,
        int take,
        bool condition)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        SetGroupTake(GetGroupByPlan(builder), take, condition);
        return builder;
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

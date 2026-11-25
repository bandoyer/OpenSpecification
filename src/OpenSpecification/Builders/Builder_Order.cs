namespace OpenSpecification;

public static partial class SpecificationBuilderExtensions
{
    /// <summary>
    /// Adds an OrderBy clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> OrderBy<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector)
        => OrderBy(builder, keySelector, true);

    /// <summary>
    /// Adds an OrderBy clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> OrderBy<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, OrderTypeEnum.OrderBy, condition);

    /// <summary>
    /// Adds an OrderBy clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> OrderBy<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector)
        => OrderBy(builder, keySelector, true);

    /// <summary>
    /// Adds an OrderBy clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> OrderBy<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, OrderTypeEnum.OrderBy, condition);

    /// <summary>
    /// Adds an OrderBy descending clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> OrderByDescending<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector)
        => OrderByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds an OrderByDescending clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> OrderByDescending<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, OrderTypeEnum.OrderByDescending, condition);

    /// <summary>
    /// Adds an OrderByDescending clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> OrderByDescending<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector)
        => OrderByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds an OrderByDescending clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> OrderByDescending<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderStart(builder, keySelector, OrderTypeEnum.OrderByDescending, condition);

    /// <summary>
    /// Adds a ThenBy clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> ThenBy<T, TResult>(
        this IOrderedSpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector)
        => ThenBy(builder, keySelector, true);

    /// <summary>
    /// Adds a ThenBy clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> ThenBy<T, TResult>(
        this IOrderedSpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, OrderTypeEnum.ThenBy, condition);

    /// <summary>
    /// Adds a ThenBy clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> ThenBy<T>(
        this IOrderedSpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector)
        => ThenBy(builder, keySelector, true);

    /// <summary>
    /// Adds a ThenBy clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> ThenBy<T>(
        this IOrderedSpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, OrderTypeEnum.ThenBy, condition);

    /// <summary>
    /// Adds a ThenByDescending clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> ThenByDescending<T, TResult>(
        this IOrderedSpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector)
        => ThenByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds a ThenByDescending clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T, TResult> ThenByDescending<T, TResult>(
        this IOrderedSpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, OrderTypeEnum.ThenByDescending, condition);

    /// <summary>
    /// Adds a ThenByDescending clause to the specification.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> ThenByDescending<T>(
        this IOrderedSpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector)
        => ThenByDescending(builder, keySelector, true);

    /// <summary>
    /// Adds a ThenByDescending clause to the specification if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The ordered specification builder.</param>
    /// <param name="keySelector">The key selector expression.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated ordered specification builder.</returns>
    public static IOrderedSpecificationBuilder<T> ThenByDescending<T>(
        this IOrderedSpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        bool condition)
        => AddOrderContinuation(builder, keySelector, OrderTypeEnum.ThenByDescending, condition);

    private static IOrderedSpecificationBuilder<T, TResult> AddOrderStart<T, TResult>(
        ISpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        if (condition)
        {
            var expr = new OrderExpressionInfo<T>(keySelector, orderType);
            builder.Specification.Add(expr);
            return builder is SpecificationBuilder<T, TResult> { IsChainDiscarded: false } existing
                ? existing
                : new SpecificationBuilder<T, TResult>(builder.Specification);
        }

        return new SpecificationBuilder<T, TResult>(builder.Specification, isChainDiscarded: true);
    }

    private static IOrderedSpecificationBuilder<T> AddOrderStart<T>(
        ISpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        if (condition)
        {
            var expr = new OrderExpressionInfo<T>(keySelector, orderType);
            builder.Specification.Add(expr);
            return builder is SpecificationBuilder<T> { IsChainDiscarded: false } existing
                ? existing
                : new SpecificationBuilder<T>(builder.Specification);
        }

        return new SpecificationBuilder<T>(builder.Specification, isChainDiscarded: true);
    }

    private static IOrderedSpecificationBuilder<T, TResult> AddOrderContinuation<T, TResult>(
        IOrderedSpecificationBuilder<T, TResult> builder,
        Expression<Func<T, object?>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        var isDiscarded = builder is SpecificationBuilder<T> { IsChainDiscarded: true };
        if (condition && !isDiscarded)
        {
            var expr = new OrderExpressionInfo<T>(keySelector, orderType);
            builder.Specification.Add(expr);
            return builder;
        }

        return isDiscarded ? builder : new SpecificationBuilder<T, TResult>(builder.Specification, isChainDiscarded: true);
    }

    private static IOrderedSpecificationBuilder<T> AddOrderContinuation<T>(
        IOrderedSpecificationBuilder<T> builder,
        Expression<Func<T, object?>> keySelector,
        OrderTypeEnum orderType,
        bool condition)
    {
        var isDiscarded = builder is SpecificationBuilder<T> { IsChainDiscarded: true };
        if (condition && !isDiscarded)
        {
            var expr = new OrderExpressionInfo<T>(keySelector, orderType);
            builder.Specification.Add(expr);
            return builder;
        }

        return isDiscarded ? builder : new SpecificationBuilder<T>(builder.Specification, isChainDiscarded: true);
    }
}

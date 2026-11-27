namespace OpenSpecification;

public static partial class SpecificationBuilderExtensions
{
    /// <summary>
    /// Configures the specification to deduplicate query results.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <returns>The updated specification builder.</returns>
    public static ISpecificationBuilder<T, TResult> Distinct<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder)
    {
        Distinct((ISpecificationBuilder<T>)builder, true);
        return builder;
    }

    /// <summary>
    /// Configures the specification to deduplicate query results if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated specification builder.</returns>
    public static ISpecificationBuilder<T, TResult> Distinct<T, TResult>(
        this ISpecificationBuilder<T, TResult> builder,
        bool condition)
    {
        Distinct((ISpecificationBuilder<T>)builder, condition);
        return builder;
    }

    /// <summary>
    /// Configures the specification to deduplicate query results.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <returns>The updated specification builder.</returns>
    public static ISpecificationBuilder<T> Distinct<T>(
        this ISpecificationBuilder<T> builder)
        => Distinct(builder, true);

    /// <summary>
    /// Configures the specification to deduplicate query results if the condition is true.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="builder">The specification builder.</param>
    /// <param name="condition">The condition to evaluate.</param>
    /// <returns>The updated specification builder.</returns>
    public static ISpecificationBuilder<T> Distinct<T>(
        this ISpecificationBuilder<T> builder,
        bool condition)
    {
        if (condition)
        {
            builder.Specification.IsDistinct = true;
        }

        return builder;
    }
}

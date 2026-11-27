namespace OpenSpecification;

/// <summary>
/// Exception thrown when <c>GroupBy()</c> is used more than once in the same specification.
/// </summary>
public class DuplicateGroupByException : Exception
{
    private const string MESSAGE = "Duplicate use of GroupBy(). Ensure you don't use GroupBy() more than once in the same specification!";

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateGroupByException"/> class.
    /// </summary>
    public DuplicateGroupByException()
        : base(MESSAGE)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateGroupByException"/> class with a specified inner exception.
    /// </summary>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DuplicateGroupByException(Exception innerException)
        : base(MESSAGE, innerException)
    {
    }
}

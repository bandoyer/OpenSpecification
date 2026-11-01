namespace OpenSpecification.Paging;

/// <summary>
/// The page size a paged query uses when the filter gives none, and the largest page size it allows.
/// </summary>
public sealed class PaginationSettings
{
    /// <summary>
    /// Gets the settings a paged query uses unless it's given others: a default page size of 10, and a limit of 100.
    /// </summary>
    public static PaginationSettings Default { get; } = new(10, 100);

    /// <summary>
    /// Initializes the settings.
    /// </summary>
    /// <param name="defaultPageSize">The page size for a filter with a missing or non-positive page size.</param>
    /// <param name="pageSizeLimit">The largest page size. A larger page size in a filter becomes this one.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="defaultPageSize"/> is less than 1, or <paramref name="pageSizeLimit"/> is less than
    /// <paramref name="defaultPageSize"/>.
    /// </exception>
    public PaginationSettings(int defaultPageSize, int pageSizeLimit)
    {
        if (defaultPageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(defaultPageSize), defaultPageSize, "The default page size must be 1 or more.");
        if (pageSizeLimit < defaultPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSizeLimit), pageSizeLimit, "The page size limit must be at least the default page size.");

        DefaultPageSize = defaultPageSize;
        PageSizeLimit = pageSizeLimit;
    }

    /// <summary>
    /// Gets the page size for a filter with a missing or non-positive page size.
    /// </summary>
    public int DefaultPageSize { get; }

    /// <summary>
    /// Gets the largest page size.
    /// </summary>
    public int PageSizeLimit { get; }
}

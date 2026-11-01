namespace OpenSpecification.Paging;

/// <summary>
/// The details of one page of a query's results.
/// </summary>
public sealed class Pagination
{
    /// <summary>
    /// Initializes the details with the values given, as a serializer does when it reads them back.
    /// <see cref="Create"/> computes them from a count and a filter.
    /// </summary>
    /// <param name="page">The page number, counted from 1.</param>
    /// <param name="pageSize">The number of items on a page.</param>
    /// <param name="totalItems">The number of items on every page together.</param>
    /// <param name="totalPages">The number of pages.</param>
    /// <param name="hasPrevious">Whether a page comes before this one.</param>
    /// <param name="hasNext">Whether a page comes after this one.</param>
    public Pagination(int page, int pageSize, int totalItems, int totalPages, bool hasPrevious, bool hasNext)
    {
        Page = page;
        PageSize = pageSize;
        TotalItems = totalItems;
        TotalPages = totalPages;
        HasPrevious = hasPrevious;
        HasNext = hasNext;
    }

    /// <summary>
    /// Gets the page number, counted from 1.
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Gets the number of items on a page. The last page can hold fewer.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the number of items on every page together.
    /// </summary>
    public int TotalItems { get; }

    /// <summary>
    /// Gets the number of pages. A query with no items has one empty page.
    /// </summary>
    public int TotalPages { get; }

    /// <summary>
    /// Gets whether a page comes before this one.
    /// </summary>
    public bool HasPrevious { get; }

    /// <summary>
    /// Gets whether a page comes after this one.
    /// </summary>
    public bool HasNext { get; }

    // The number of items before this page.
    internal int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Computes the details of the page <paramref name="filter"/> asks for, out of <paramref name="totalItems"/>
    /// items. An out-of-range page or page size is clamped, not refused: a missing or non-positive page is page 1, a
    /// page past the last is the last, a missing or non-positive page size is the settings' default, and a page size
    /// past the settings' limit is the limit. No items gives one empty page.
    /// </summary>
    /// <param name="totalItems">The number of items on every page together. A negative count is 0.</param>
    /// <param name="filter">The page and page size the caller asks for.</param>
    /// <param name="settings">The default page size and the page size limit.</param>
    /// <returns>The page's details.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="filter"/> or <paramref name="settings"/> is null.</exception>
    public static Pagination Create(int totalItems, IPagingFilter filter, PaginationSettings settings)
    {
        _ = filter ?? throw new ArgumentNullException(nameof(filter));
        _ = settings ?? throw new ArgumentNullException(nameof(settings));

        if (totalItems < 0) totalItems = 0;

        var pageSize = filter.PageSize is not int size || size <= 0
            ? settings.DefaultPageSize
            : Math.Min(size, settings.PageSizeLimit);

        // (totalItems - 1) / pageSize + 1 rounds up without overflowing near int.MaxValue.
        var totalPages = totalItems == 0 ? 1 : (totalItems - 1) / pageSize + 1;

        var page = filter.Page is not int number || number <= 0
            ? 1
            : Math.Min(number, totalPages);

        return new Pagination(page, pageSize, totalItems, totalPages, page > 1, page < totalPages);
    }
}

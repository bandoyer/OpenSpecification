namespace OpenSpecification.Paging;

/// <summary>
/// One page of a query's results, and the page's details.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>
    /// Initializes the page.
    /// </summary>
    /// <param name="items">The page's items.</param>
    /// <param name="pagination">The page's details.</param>
    public PagedResult(List<T> items, Pagination pagination)
    {
        Items = items;
        Pagination = pagination;
    }

    /// <summary>
    /// Gets the page's items.
    /// </summary>
    public List<T> Items { get; }

    /// <summary>
    /// Gets the page's details.
    /// </summary>
    public Pagination Pagination { get; }
}

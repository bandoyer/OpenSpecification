namespace OpenSpecification.Paging;

/// <summary>
/// The page a caller asks for. Implement it on a request class of your own, such as the one a web API binds its query
/// string to, or use <see cref="PagingFilter"/>.
/// </summary>
public interface IPagingFilter
{
    /// <summary>
    /// Gets the page number, counted from 1. A missing or non-positive page is the first page, and a page past the last
    /// is the last page.
    /// </summary>
    int? Page { get; }

    /// <summary>
    /// Gets the number of items on a page. A missing or non-positive page size is
    /// <see cref="PaginationSettings.DefaultPageSize"/>, and a page size past
    /// <see cref="PaginationSettings.PageSizeLimit"/> is the limit.
    /// </summary>
    int? PageSize { get; }
}

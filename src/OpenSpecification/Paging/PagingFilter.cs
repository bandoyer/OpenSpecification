namespace OpenSpecification.Paging;

/// <summary>
/// A page and page size, both optional. Derive a request class from it, or implement <see cref="IPagingFilter"/>
/// on a class or record of your own.
/// </summary>
public class PagingFilter : IPagingFilter
{
    /// <inheritdoc/>
    public int? Page { get; set; }

    /// <inheritdoc/>
    public int? PageSize { get; set; }
}

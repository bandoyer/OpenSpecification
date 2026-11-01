// A consumer's own paging classes, named as upstream's Sample App3 named them, in a namespace of the consumer's own.
// Builds against every tree.
namespace Consumer.Web;

public class PaginationSettings(int defaultPageSize, int defaultPageSizeLimit)
{
    public int DefaultPageSize { get; } = defaultPageSize;
    public int DefaultPageSizeLimit { get; } = defaultPageSizeLimit;
}

public class Pagination(int totalItems, int page)
{
    public int TotalItems { get; } = totalItems;
    public int Page { get; } = page;
}

// The consumer's own request class, which implements the library's IPagingFilter. Needs a tree with #59.
using OpenSpecification.Paging;

namespace Consumer.Web;

public class CountryRequest : IPagingFilter
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? Name { get; set; }
}

using OpenSpecification.Paging;

namespace OpenSpecification.Sample.Domain.Filters;

public record BaseFilter : IPagingFilter
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? SortBy { get; set; }
    public string? OrderBy { get; set; }
}

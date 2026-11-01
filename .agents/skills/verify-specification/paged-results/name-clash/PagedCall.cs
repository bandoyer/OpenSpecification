// Calls PagedListAsync and reads its result through var, with no using directive for OpenSpecification.Paging, in a
// file that also names the consumer's own Pagination (line marked CLASH). Needs a tree with #59.
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using Consumer.Web;

public static class PagedCall
{
    public static async Task<string> FirstPage(CountryRepository repository, CountryRequest request)
    {
        var page = await repository.PagedListAsync(new NamesStartingWithA(), request);
        Pagination own = new(page.Pagination.TotalItems, page.Pagination.Page); // CLASH
        return $"{page.Items.Count} of {own.TotalItems}";
    }
}

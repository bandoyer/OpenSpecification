// Imports the library's root namespace and the consumer's own, and names the consumer's Pagination and
// PaginationSettings. A library type of either name in OpenSpecification or OpenSpecification.EntityFrameworkCore would
// make each line marked CLASH fail with CS0104. Builds against every tree.
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using Consumer.Web;

public static class UsesBoth
{
    public static string Describe(ISpecification<Country> specification)
    {
        var settings = new PaginationSettings(10, 100); // CLASH
        Pagination pagination = new(25, 2); // CLASH
        return $"{specification.GetType().Name}: {settings.DefaultPageSize}, {pagination.TotalItems}";
    }
}

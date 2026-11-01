using OpenSpecification.Paging;

namespace Tests.Paging;

public class PagedResultTests
{
    [Fact]
    public void Constructor_KeepsTheItemsAndPagination()
    {
        var items = new List<string> { "a", "b" };
        var pagination = new Pagination(1, 10, 2, 1, false, false);

        var result = new PagedResult<string>(items, pagination);

        result.Items.Should().BeSameAs(items);
        result.Pagination.Should().BeSameAs(pagination);
    }

#if NET
    private const string Json = """{"items":["k","l"],"pagination":{"page":2,"pageSize":10,"totalItems":25,"totalPages":3,"hasPrevious":true,"hasNext":true}}""";

    [Fact]
    public void SystemTextJson_WritesItemsThenTheSixDetails()
    {
        var result = new PagedResult<string>(["k", "l"], new Pagination(2, 10, 25, 3, true, true));

        System.Text.Json.JsonSerializer.Serialize(result, System.Text.Json.JsonSerializerOptions.Web).Should().Be(Json);
    }

    [Fact]
    public void SystemTextJson_ReadsBackWhatItWrote()
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<PagedResult<string>>(Json, System.Text.Json.JsonSerializerOptions.Web)!;

        result.Items.Should().Equal("k", "l");
        result.Pagination.Should().BeEquivalentTo(new Pagination(2, 10, 25, 3, true, true));
    }
#endif
}

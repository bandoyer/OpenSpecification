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

    [Fact]
    public void EnsurePageable_RejectsGroupedSpecWithGroupSkipOrTake_AndAllowsGroupSkipZero()
    {
        var filter = new PagingFilter { Page = 1, PageSize = 10 };
        var settings = PaginationSettings.Default;

        var preSkip = new Specification<string, int>();
        preSkip.Query.GroupBy(x => x).Skip(1).Select(g => g.Count());

        var postSkip = new Specification<string, int>();
        postSkip.Query.GroupBy(x => x).Select(g => g.Count()).Skip(1);

        var preTakeZero = new Specification<string, int>();
        preTakeZero.Query.GroupBy(x => x).Take(0).Select(g => g.Count());

        var postTake = new Specification<string, int>();
        postTake.Query.GroupBy(x => x).Select(g => g.Count()).Take(5);

        var skipZeroOnly = new Specification<string, int>();
        skipZeroOnly.Query.GroupBy(x => x).Skip(0).Select(g => g.Count());

        Action a1 = () => preSkip.EnsurePageable(filter, settings);
        Action a2 = () => postSkip.EnsurePageable(filter, settings);
        Action a3 = () => preTakeZero.EnsurePageable(filter, settings);
        Action a4 = () => postTake.EnsurePageable(filter, settings);
        Action a5 = () => skipZeroOnly.EnsurePageable(filter, settings);

        a1.Should().Throw<ArgumentException>().WithParameterName("specification");
        a2.Should().Throw<ArgumentException>().WithParameterName("specification");
        a3.Should().Throw<ArgumentException>().WithParameterName("specification");
        a4.Should().Throw<ArgumentException>().WithParameterName("specification");
        a5.Should().NotThrow();
    }
}

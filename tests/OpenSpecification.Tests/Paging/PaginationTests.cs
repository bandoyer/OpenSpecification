using OpenSpecification.Paging;

namespace Tests.Paging;

public class PaginationTests
{
    private static readonly PaginationSettings _settings = new(10, 100);

    public record Expected(int Page, int PageSize, int TotalItems, int TotalPages, bool HasPrevious, bool HasNext);

    // Worked by hand from Sample App3's rules: a missing or non-positive page is 1, a page past the last is the last, a
    // missing or non-positive page size is the default, a page size past the limit is the limit, and no items is one
    // empty page.
    public static TheoryData<int, int?, int?, Expected> Cases => new()
    {
        { 25, 2, 10, new(2, 10, 25, 3, true, true) },
        { 25, 1, 10, new(1, 10, 25, 3, false, true) },
        { 25, 3, 10, new(3, 10, 25, 3, true, false) },
        { 25, 99, 10, new(3, 10, 25, 3, true, false) },
        { 25, 0, 10, new(1, 10, 25, 3, false, true) },
        { 25, -1, 10, new(1, 10, 25, 3, false, true) },
        { 25, null, 10, new(1, 10, 25, 3, false, true) },
        { 25, 1, 0, new(1, 10, 25, 3, false, true) },
        { 25, 1, -5, new(1, 10, 25, 3, false, true) },
        { 25, 1, null, new(1, 10, 25, 3, false, true) },
        { 25, 1, 1000, new(1, 100, 25, 1, false, false) },
        { 30, 3, 10, new(3, 10, 30, 3, true, false) },
        { 31, 4, 10, new(4, 10, 31, 4, true, false) },
        { 0, 2, 10, new(1, 10, 0, 1, false, false) },
        { -3, 1, 10, new(1, 10, 0, 1, false, false) },
        { 1, 1, 1, new(1, 1, 1, 1, false, false) },
        { int.MaxValue, int.MaxValue, 100, new(21474837, 100, int.MaxValue, 21474837, true, false) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Create_ClampsTheFilterToThePagesThatExist(int totalItems, int? page, int? pageSize, Expected expected)
    {
        var pagination = Pagination.Create(totalItems, new PagingFilter { Page = page, PageSize = pageSize }, _settings);

        new Expected(pagination.Page, pagination.PageSize, pagination.TotalItems, pagination.TotalPages, pagination.HasPrevious, pagination.HasNext)
            .Should().Be(expected);
    }

    [Fact]
    public void Create_UsesTheSettingsDefaultAndLimit()
    {
        var settings = new PaginationSettings(5, 20);

        Pagination.Create(100, new PagingFilter(), settings).PageSize.Should().Be(5);
        Pagination.Create(100, new PagingFilter { PageSize = 50 }, settings).PageSize.Should().Be(20);
        Pagination.Create(100, new PagingFilter { PageSize = 20 }, settings).PageSize.Should().Be(20);
    }

    [Fact]
    public void Create_TakesAFilterOfTheCallersOwn()
    {
        var pagination = Pagination.Create(25, new CustomerRequest(2, 10, "Ann"), _settings);

        pagination.Page.Should().Be(2);
        pagination.PageSize.Should().Be(10);
    }

    [Fact]
    public void Create_Throws_GivenNullFilter()
    {
        var action = () => Pagination.Create(25, null!, _settings);

        action.Should().Throw<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public void Create_Throws_GivenNullSettings()
    {
        var action = () => Pagination.Create(25, new PagingFilter(), null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_KeepsTheValuesItIsGiven()
    {
        var pagination = new Pagination(2, 10, 25, 3, true, true);

        new Expected(pagination.Page, pagination.PageSize, pagination.TotalItems, pagination.TotalPages, pagination.HasPrevious, pagination.HasNext)
            .Should().Be(new Expected(2, 10, 25, 3, true, true));
    }

    public record CustomerRequest(int? Page, int? PageSize, string? Name) : IPagingFilter;
}

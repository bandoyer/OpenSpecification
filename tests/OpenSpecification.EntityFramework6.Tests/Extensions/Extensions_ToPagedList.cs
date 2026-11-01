using OpenSpecification.Paging;

namespace Tests.Extensions;

[Collection("SharedCollection")]
public class Extensions_ToPagedList(TestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task ToPagedListAsync_ReturnsTheAskedPageAndItsDetails()
    {
        await SeedMatchesAsync();

        var result = await DbContext.Countries.ToPagedListAsync(Matches(), new PagingFilter { Page = 2, PageSize = 2 });

        result.Items.Select(x => x.Name).Should().Equal("m3", "m4");
        result.Pagination.Page.Should().Be(2);
        result.Pagination.TotalItems.Should().Be(5);
        result.Pagination.TotalPages.Should().Be(3);
        result.Pagination.HasPrevious.Should().BeTrue();
        result.Pagination.HasNext.Should().BeTrue();
    }

    [Fact]
    public async Task ToPagedListAsync_UsesTheSettingsGiven()
    {
        await SeedMatchesAsync();
        var settings = new PaginationSettings(2, 3);

        var byDefault = await DbContext.Countries.ToPagedListAsync(Matches(), new PagingFilter(), settings);
        var limited = await DbContext.Countries.ToPagedListAsync(Matches(), new PagingFilter { PageSize = 50 }, settings);

        byDefault.Items.Select(x => x.Name).Should().Equal("m1", "m2");
        limited.Items.Select(x => x.Name).Should().Equal("m1", "m2", "m3");
        limited.Pagination.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task ToPagedListAsync_UsesTheDefaultSettings_GivenNoSettings()
    {
        await SeedMatchesAsync();

        var result = await DbContext.Countries.ToPagedListAsync(Matches(), new PagingFilter { PageSize = 1000 });

        result.Pagination.PageSize.Should().Be(100);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task ToPagedListAsync_ReturnsTheAskedPageOfProjections_GivenSelect()
    {
        await SeedMatchesAsync();
        var spec = new Specification<Country, string?>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name);
        spec.Query.Select(x => x.Name);

        var result = await DbContext.Countries.ToPagedListAsync(spec, new PagingFilter { Page = 3, PageSize = 2 }, new PaginationSettings(2, 10));

        result.Items.Should().Equal("m5");
        result.Pagination.TotalItems.Should().Be(5);
    }

    // EF6 refuses Skip on rows it can't see as ordered, and a specification can't order the rows SelectMany flattens.
    [Fact]
    public async Task ToPagedListAsync_Throws_GivenSelectMany()
    {
        var spec = new Specification<Company, string?>();
        spec.Query.OrderBy(x => x.Name);
        spec.Query.SelectMany(x => x.Stores.Select(s => s.Name));

        var action = () => DbContext.Companies.ToPagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 4 });

        await action.Should().ThrowAsync<NotSupportedException>().WithMessage("*SelectMany*");
    }

    [Fact]
    public async Task ToPagedListAsync_Throws_GivenSpecWithSkip()
    {
        var spec = new Specification<Country>();
        spec.Query.OrderBy(x => x.Name).Skip(1);

        var action = () => DbContext.Countries.ToPagedListAsync(spec, new PagingFilter());

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
    }

    [Fact]
    public async Task ToPagedListAsync_Throws_GivenNullSettings()
    {
        var action = () => DbContext.Countries.ToPagedListAsync(Matches(), new PagingFilter(), null!);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("settings");
    }

    private static Specification<Country> Matches()
    {
        var spec = new Specification<Country>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name);
        return spec;
    }

    private Task SeedMatchesAsync() => SeedRangeAsync<Country>(
    [
        new() { Name = "m4" },
        new() { Name = "a" },
        new() { Name = "m1" },
        new() { Name = "m5" },
        new() { Name = "m2" },
        new() { Name = "m3" },
    ]);
}

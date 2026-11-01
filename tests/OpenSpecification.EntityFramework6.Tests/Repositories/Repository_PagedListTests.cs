using OpenSpecification.Paging;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class Repository_PagedListTests(TestFactory factory) : IntegrationTest(factory)
{
    public record Expected(int Page, int PageSize, int TotalItems, int TotalPages, bool HasPrevious, bool HasNext);

    [Fact]
    public async Task PagedListAsync_ReturnsTheAskedPageAndItsDetails()
    {
        await SeedMatchesAsync();
        var repo = new Repository<Country>(DbContext);

        var result = await repo.PagedListAsync(Matches(), new PagingFilter { Page = 2, PageSize = 10 });

        result.Items.Select(x => x.Name).Should().Equal(Names(11, 20));
        Details(result.Pagination).Should().Be(new Expected(2, 10, 25, 3, true, true));
    }

    [Fact]
    public async Task PagedListAsync_ReturnsTheLastPage_GivenPagePastTheLast()
    {
        await SeedMatchesAsync();
        var repo = new Repository<Country>(DbContext);

        var result = await repo.PagedListAsync(Matches(), new PagingFilter { Page = 99, PageSize = 10 });

        result.Items.Select(x => x.Name).Should().Equal(Names(21, 25));
        Details(result.Pagination).Should().Be(new Expected(3, 10, 25, 3, true, false));
    }

    [Fact]
    public async Task PagedListAsync_ReturnsOneEmptyPage_GivenNoMatch()
    {
        await SeedMatchesAsync();
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country>();
        spec.Query.Where(x => x.Name == "peru").OrderBy(x => x.Name);

        var result = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 });

        result.Items.Should().BeEmpty();
        Details(result.Pagination).Should().Be(new Expected(1, 10, 0, 1, false, false));
    }

    [Fact]
    public async Task PagedListAsync_IgnoresPostProcessingAction()
    {
        await SeedMatchesAsync();
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name).PostProcessingAction(x => x.Reverse());

        var result = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 });

        result.Items.Select(x => x.Name).Should().Equal(Names(11, 20));
    }

    [Fact]
    public async Task PagedListAsync_UsesTheRepositorysSettings()
    {
        await SeedMatchesAsync();
        var repo = new SmallPageRepository(DbContext);

        var byDefault = await repo.PagedListAsync(Matches(), new PagingFilter { Page = 1 });
        var limited = await repo.PagedListAsync(Matches(), new PagingFilter { Page = 1, PageSize = 50 });

        Details(byDefault.Pagination).Should().Be(new Expected(1, 5, 25, 5, false, true));
        Details(limited.Pagination).Should().Be(new Expected(1, 20, 25, 2, false, true));
        limited.Items.Should().HaveCount(20);
    }

    [Fact]
    public async Task PagedListAsync_AppliesIncludes()
    {
        await SeedCompaniesAsync();
        var repo = new Repository<Company>(DbContext);
        var spec = new Specification<Company>();
        spec.Query.Include(x => x.Stores).OrderBy(x => x.Name);

        var result = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });

        result.Items.Select(x => (x.Name, x.Stores.Count)).Should().Equal(("c3", 2));
        Details(result.Pagination).Should().Be(new Expected(2, 2, 3, 2, true, false));
    }

    [Fact]
    public async Task PagedListAsync_ReturnsTheAskedPageOfProjections_GivenSelect()
    {
        await SeedMatchesAsync();
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country, string?>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name);
        spec.Query.Select(x => x.Name);

        var result = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 });

        result.Items.Should().Equal(Names(11, 20));
        Details(result.Pagination).Should().Be(new Expected(2, 10, 25, 3, true, true));
    }

    // EF6 refuses Skip on rows it can't see as ordered, and a specification can't order the rows SelectMany flattens.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PagedListAsync_Throws_GivenSelectMany(int page)
    {
        await SeedCompaniesAsync();
        var repo = new Repository<Company>(DbContext);
        var spec = new Specification<Company, string?>();
        spec.Query.OrderBy(x => x.Name);
        spec.Query.SelectMany(x => x.Stores.Select(s => s.Name));

        var action = () => repo.PagedListAsync(spec, new PagingFilter { Page = page, PageSize = 4 });

        await action.Should().ThrowAsync<NotSupportedException>().WithMessage("*SelectMany*");
    }

    [Fact]
    public async Task PagedListAsync_Throws_GivenSpecWithTake()
    {
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country>();
        spec.Query.OrderBy(x => x.Name).Take(5);

        var action = () => repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 });

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
    }

    [Fact]
    public async Task PagedListAsync_Throws_GivenProjectionSpecWithSkip()
    {
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country, string?>();
        spec.Query.OrderBy(x => x.Name).Skip(5);
        spec.Query.Select(x => x.Name);

        var action = () => repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 10 });

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
    }

    [Fact]
    public async Task PagedListAsync_Throws_GivenNullFilter()
    {
        var repo = new Repository<Country>(DbContext);

        var action = () => repo.PagedListAsync(Matches(), null!);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    private static Expected Details(Pagination p) => new(p.Page, p.PageSize, p.TotalItems, p.TotalPages, p.HasPrevious, p.HasNext);

    private static Specification<Country> Matches()
    {
        var spec = new Specification<Country>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name);
        return spec;
    }

    private static List<string> Names(int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => $"m{i:00}").ToList();

    // 25 countries named m01 to m25, out of name order, among three that don't match.
    private Task SeedMatchesAsync() => SeedRangeAsync(
        new[] { 7, 19, 1, 25, 13, 4, 22, 10, 16, 2, 24, 8, 14, 20, 5, 11, 23, 3, 17, 9, 21, 6, 15, 12, 18 }
            .Select(i => new Country { Name = $"m{i:00}" })
            .Concat([new Country { Name = "a" }, new Country { Name = "b" }, new Country { Name = "z" }])
            .ToArray());

    // Three companies, c1 to c3, with two stores each.
    private Task SeedCompaniesAsync()
    {
        var country = new Country { Name = "x" };
        return SeedRangeAsync(new[] { "c2", "c3", "c1" }
            .Select(name => new Company { Name = name, Country = country, Stores = [new Store { Name = $"{name}s1" }, new Store { Name = $"{name}s2" }] })
            .ToArray());
    }

    private sealed class SmallPageRepository(TestDbContext context) : RepositoryBase<Country>(context)
    {
        protected override PaginationSettings PaginationSettings { get; } = new(5, 20);
    }
}

using OpenSpecification.Paging;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class ContextFactoryRepository_PagedListTests(TestFactory factory) : IntegrationTest(factory)
{
    private readonly RecordingContextFactory _contextFactory = new(factory.DbContextOptions);

    [Fact]
    public async Task PagedListAsync_ReturnsTheAskedPageAndDisposesContext()
    {
        await SeedMatchesAsync();
        var repo = new ContextFactoryRepository(_contextFactory);

        var result = await repo.PagedListAsync(Matches(), new PagingFilter { Page = 2, PageSize = 2 });

        result.Items.Select(x => x.Name).Should().Equal("m3", "m4");
        result.Pagination.TotalItems.Should().Be(5);
        result.Pagination.TotalPages.Should().Be(3);
        _contextFactory.Made.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task PagedListAsync_ReturnsTheAskedPageOfProjections_GivenSelect()
    {
        await SeedMatchesAsync();
        var repo = new ContextFactoryRepository(_contextFactory);
        var spec = new Specification<Country, string?>();
        spec.Query.Where(x => x.Name!.StartsWith("m")).OrderBy(x => x.Name);
        spec.Query.Select(x => x.Name);

        var result = await repo.PagedListAsync(spec, new PagingFilter { Page = 3, PageSize = 2 });

        result.Items.Should().Equal("m5");
        result.Pagination.HasNext.Should().BeFalse();
        _contextFactory.Made.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task PagedListAsync_ThrowsBeforeMakingAContext_GivenSpecWithSkip()
    {
        var repo = new ContextFactoryRepository(_contextFactory);
        var spec = new Specification<Country>();
        spec.Query.OrderBy(x => x.Name).Skip(5);

        var action = () => repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
        _contextFactory.Made.Should().BeEmpty();
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

    private sealed class ContextFactoryRepository(IDbContextFactory<TestDbContext> dbContextFactory)
        : ContextFactoryRepositoryBaseOfT<Country, TestDbContext>(dbContextFactory);
}

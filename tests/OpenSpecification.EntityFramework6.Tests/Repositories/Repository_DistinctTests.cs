using System.Data.Entity;
using OpenSpecification.Paging;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class Repository_DistinctTests(TestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task EntityDistinct_WorksAcrossRepositoryAndDbSetMethods()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);

        var spec = new Specification<Country>();
        spec.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .OrderBy(x => x.Name)
            .Skip(1)
            .Take(2);

        var singleSpec = new SingleResultSpecification<Country>();
        singleSpec.Query.Where(x => x.Name == "unique").Distinct();

        var pageSpec = new Specification<Country>();
        pageSpec.Query.Where(x => x.Name!.StartsWith("d")).Distinct().OrderBy(x => x.Name);

        var list = await repo.ListAsync(spec);
        var count = await repo.CountAsync(spec);
        var any = await repo.AnyAsync(spec);
        var first = await repo.FirstOrDefaultAsync(spec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var withSpecCount = await DbContext.Countries.WithSpecification(spec).CountAsync();
        var repoPage = await repo.PagedListAsync(pageSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var dbSetPage = await DbContext.Countries.ToPagedListAsync(pageSpec, new PagingFilter { Page = 1, PageSize = 2 });

        list.Select(x => x.Name).Should().Equal("d1", "d2");
        count.Should().Be(6);
        any.Should().BeTrue();
        first!.Name.Should().Be("d1");
        single!.Name.Should().Be("unique");
        withSpecCount.Should().Be(2);
        repoPage.Pagination.TotalItems.Should().Be(6);
        repoPage.Items.Should().HaveCount(2);
        dbSetPage.Pagination.TotalItems.Should().Be(6);
    }

    [Fact]
    public async Task ProjectionSelectDistinct_DeduplicatesAndAppliesTakeAndPostProcessing()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);

        var allDistinct = new Specification<Country, string?>();
        allDistinct.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .Select(x => x.Name);

        var taken = new Specification<Country, string?>();
        taken.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .Skip(0)
            .Take(2)
            .PostProcessingAction(items => items.Select(x => $"*{x}"))
            .Select(x => x.Name);

        var singleSpec = new SingleResultSpecification<Country, string?>();
        singleSpec.Query.Where(x => x.Name == "d1").Distinct().Select(x => x.Name);

        var distinctNames = await repo.ListAsync(allDistinct);
        var takenRepo = await repo.ListAsync(taken);
        var takenDbSet = await DbContext.Countries.ToListAsync(taken);
        var takenEnum = (await DbContext.Countries.ToEnumerableAsync(taken)).ToList();
        var first = await repo.FirstOrDefaultAsync(taken);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var withSpecCount = await DbContext.Countries.WithSpecification(allDistinct).CountAsync();

        distinctNames.Should().BeEquivalentTo(["d1", "d2", "d3"]);
        takenRepo.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        takenDbSet.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        takenEnum.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        first.Should().NotBeNull().And.NotStartWith("*");
        single.Should().Be("d1");
        withSpecCount.Should().Be(3);
    }

    [Fact]
    public async Task ProjectionSelectManyDistinct_DeduplicatesFlattenedValues()
    {
        await SeedCompanyStoreDuplicatesAsync();
        var repo = new Repository<Company>(DbContext);

        var spec = new Specification<Company, string?>();
        spec.Query
            .Distinct()
            .PostProcessingAction(items => items.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"#{x}"))
            .SelectMany(c => c.Stores.Select(s => s.Name));

        var taken = new Specification<Company, string?>();
        taken.Query
            .Distinct()
            .Take(2)
            .SelectMany(c => c.Stores.Select(s => s.Name));

        var all = await repo.ListAsync(spec);
        var take = await repo.ListAsync(taken);
        var withSpecCount = await DbContext.Companies.WithSpecification(spec).CountAsync();

        all.Should().Equal("#s1", "#s2", "#s3", "#s4");
        take.Should().HaveCount(2);
        withSpecCount.Should().Be(4);
    }

    [Fact]
    public async Task UpcastCountAndAny_StayEntityCriteriaOnly_GivenDistinctProjection()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);

        var spec = new Specification<Country, string?>();
        spec.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .Select(x => x.Name);

        var count = await repo.CountAsync((ISpecification<Country>)spec);
        var any = await repo.AnyAsync((ISpecification<Country>)spec);

        count.Should().Be(6);
        any.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PagedListAsync_AndToPagedListAsync_ThrowUpFront_GivenDistinctSelectProjection(int page)
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country, string?>();
        spec.Query.Distinct().Select(x => x.Name);

        var repoAction = () => repo.PagedListAsync(spec, new PagingFilter { Page = page, PageSize = 2 });
        var dbSetAction = () => DbContext.Countries.ToPagedListAsync(spec, new PagingFilter { Page = page, PageSize = 2 });

        await repoAction.Should().ThrowAsync<NotSupportedException>();
        await dbSetAction.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PagedListAsync_AndToPagedListAsync_ThrowUpFront_GivenDistinctSelectManyProjection(int page)
    {
        await SeedCompanyStoreDuplicatesAsync();
        var repo = new Repository<Company>(DbContext);
        var spec = new Specification<Company, string?>();
        spec.Query.Distinct().SelectMany(c => c.Stores.Select(s => s.Name));

        var repoAction = () => repo.PagedListAsync(spec, new PagingFilter { Page = page, PageSize = 2 });
        var dbSetAction = () => DbContext.Companies.ToPagedListAsync(spec, new PagingFilter { Page = page, PageSize = 2 });

        await repoAction.Should().ThrowAsync<NotSupportedException>();
        await dbSetAction.Should().ThrowAsync<NotSupportedException>();
    }

    private Task SeedDuplicatesAsync() => SeedRangeAsync<Country>(
    [
        new() { Name = "d1" },
        new() { Name = "d1" },
        new() { Name = "d2" },
        new() { Name = "d2" },
        new() { Name = "d3" },
        new() { Name = "d3" },
        new() { Name = "unique" },
    ]);

    private Task SeedCompanyStoreDuplicatesAsync()
    {
        var country = new Country { Name = "x" };
        return SeedRangeAsync(
        [
            new Company { Name = "c1", Country = country, Stores = [new Store { Name = "s1" }, new Store { Name = "s2" }] },
            new Company { Name = "c2", Country = country, Stores = [new Store { Name = "s2" }, new Store { Name = "s3" }] },
            new Company { Name = "c3", Country = country, Stores = [new Store { Name = "s3" }, new Store { Name = "s4" }] },
        ]);
    }
}

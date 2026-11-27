using OpenSpecification.Paging;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class Repository_DistinctTests(TestFactory factory) : IntegrationTest(factory)
{
    private readonly RecordingContextFactory _contextFactory = new(factory.DbContextOptions);

    [Fact]
    public async Task EntityDistinct_WorksAcrossRepositoryAndDbSetMethods()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);
        var factoryRepo = new CountryFactoryRepository(_contextFactory);

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
        var streamed = new List<string?>();
        await foreach (var c in repo.AsAsyncEnumerable(spec))
        {
            streamed.Add(c.Name);
        }

        var withSpecCount = await DbContext.Countries.WithSpecification(spec).CountAsync();
        var repoPage = await repo.PagedListAsync(pageSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var factoryPage = await factoryRepo.PagedListAsync(pageSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var dbSetPage = await DbContext.Countries.ToPagedListAsync(pageSpec, new PagingFilter { Page = 1, PageSize = 2 });

        list.Select(x => x.Name).Should().Equal("d1", "d2");
        count.Should().Be(6);
        any.Should().BeTrue();
        first!.Name.Should().Be("d1");
        single!.Name.Should().Be("unique");
        streamed.Should().Equal("d1", "d2");
        withSpecCount.Should().Be(2);
        repoPage.Pagination.TotalItems.Should().Be(6);
        repoPage.Items.Should().HaveCount(2);
        factoryPage.Pagination.TotalItems.Should().Be(6);
        dbSetPage.Pagination.TotalItems.Should().Be(6);
    }

    [Fact]
    public async Task ProjectionSelectDistinct_DeduplicatesAndAppliesSkipTakeAndPostProcessing()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);

        var allDistinct = new Specification<Country, string?>();
        allDistinct.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .Select(x => x.Name);

        var sliced = new Specification<Country, string?>();
        sliced.Query
            .Where(x => x.Name!.StartsWith("d"))
            .Distinct()
            .Skip(1)
            .Take(2)
            .PostProcessingAction(items => items.Select(x => $"*{x}"))
            .Select(x => x.Name);

        var singleSpec = new SingleResultSpecification<Country, string?>();
        singleSpec.Query.Where(x => x.Name == "d1").Distinct().Select(x => x.Name);

        var distinctNames = await repo.ListAsync(allDistinct);
        var slicedRepo = await repo.ListAsync(sliced);
        var slicedDbSet = await DbContext.Countries.ToListAsync(sliced);
        var slicedEnum = (await DbContext.Countries.ToEnumerableAsync(sliced)).ToList();
        var first = await repo.FirstOrDefaultAsync(sliced);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var streamed = new List<string?>();
        await foreach (var name in repo.AsAsyncEnumerable(sliced))
        {
            streamed.Add(name);
        }

        distinctNames.Should().BeEquivalentTo(["d1", "d2", "d3"]);
        slicedRepo.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        slicedDbSet.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        slicedEnum.Should().HaveCount(2).And.OnlyContain(x => x!.StartsWith('*'));
        first.Should().NotBeNull().And.NotStartWith("*");
        single.Should().Be("d1");
        streamed.Should().HaveCount(2).And.OnlyContain(x => !x!.StartsWith('*'));
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

        var sliced = new Specification<Company, string?>();
        sliced.Query
            .Distinct()
            .Skip(1)
            .Take(2)
            .SelectMany(c => c.Stores.Select(s => s.Name));

        var all = await repo.ListAsync(spec);
        var slice = await repo.ListAsync(sliced);
        var withSpecCount = await DbContext.Companies.WithSpecification(spec).CountAsync();

        all.Should().Equal("#s1", "#s2", "#s3", "#s4");
        slice.Should().HaveCount(2);
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

    [Fact]
    public async Task PagedListAsync_CountsAndPagesDistinctSelectProjection_AcrossAllEntryPointsAndCustomApplySpecification()
    {
        await SeedDuplicatesAsync();
        var repo = new Repository<Country>(DbContext);
        var factoryRepo = new CountryFactoryRepository(_contextFactory);
        var customRepo = new DPrefixCountryRepository(DbContext);

        var allSpec = new Specification<Country, string?>();
        allSpec.Query
            .Distinct()
            .PostProcessingAction(items => items.Select(x => $"IGNORED-{x}"))
            .Select(x => x.Name);

        var p1 = await repo.PagedListAsync(allSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var p2 = await repo.PagedListAsync(allSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var clamped = await repo.PagedListAsync(allSpec, new PagingFilter { Page = 99, PageSize = 2 });
        var factoryP1 = await factoryRepo.PagedListAsync(allSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var dbSetP2 = await DbContext.Countries.ToPagedListAsync(allSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var customP1 = await customRepo.PagedListAsync(allSpec, new PagingFilter { Page = 1, PageSize = 2 });

        p1.Pagination.TotalItems.Should().Be(4);
        p1.Pagination.TotalPages.Should().Be(2);
        p1.Items.Should().HaveCount(2).And.OnlyContain(x => !x!.StartsWith("IGNORED-"));
        p2.Pagination.Page.Should().Be(2);
        p2.Items.Should().HaveCount(2);
        clamped.Pagination.Page.Should().Be(2);
        clamped.Pagination.HasNext.Should().BeFalse();
        factoryP1.Pagination.TotalItems.Should().Be(4);
        dbSetP2.Pagination.TotalItems.Should().Be(4);
        customP1.Pagination.TotalItems.Should().Be(3);
        customP1.Pagination.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task PagedListAsync_CountsAndPagesDistinctSelectManyProjection()
    {
        await SeedCompanyStoreDuplicatesAsync();
        var repo = new Repository<Company>(DbContext);
        var factoryRepo = new CompanyFactoryRepository(_contextFactory);

        var spec = new Specification<Company, string?>();
        spec.Query
            .Distinct()
            .SelectMany(c => c.Stores.Select(s => s.Name));

        var p1 = await repo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var p2 = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });
        var factoryP1 = await factoryRepo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var dbSetP2 = await DbContext.Companies.ToPagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });

        p1.Pagination.TotalItems.Should().Be(4);
        p1.Pagination.TotalPages.Should().Be(2);
        p1.Items.Should().HaveCount(2);
        p2.Items.Should().HaveCount(2);
        p2.Pagination.HasNext.Should().BeFalse();
        factoryP1.Pagination.TotalItems.Should().Be(4);
        dbSetP2.Pagination.TotalItems.Should().Be(4);
    }

    [Fact]
    public async Task PagedListAsync_ThrowsArgumentException_GivenDistinctProjectionWithSkipOrTake()
    {
        var repo = new Repository<Country>(DbContext);
        var spec = new Specification<Country, string?>();
        spec.Query.Distinct().Take(2).Select(x => x.Name);

        var action = () => repo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
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

    private sealed class CountryFactoryRepository(IDbContextFactory<TestDbContext> dbContextFactory)
        : ContextFactoryRepositoryBaseOfT<Country, TestDbContext>(dbContextFactory);

    private sealed class CompanyFactoryRepository(IDbContextFactory<TestDbContext> dbContextFactory)
        : ContextFactoryRepositoryBaseOfT<Company, TestDbContext>(dbContextFactory);

    private sealed class DPrefixCountryRepository(TestDbContext context) : RepositoryBase<Country>(context)
    {
        protected override IQueryable<Country> ApplySpecification(ISpecification<Country> specification, bool evaluateCriteriaOnly = false)
            => SpecificationEvaluator.GetQuery(DbContext.Set<Country>().Where(c => c.Name!.StartsWith("d")), specification, evaluateCriteriaOnly);

        protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<Country, TResult> specification)
            => SpecificationEvaluator.GetQuery(DbContext.Set<Country>().Where(c => c.Name!.StartsWith("d")), specification);
    }
}

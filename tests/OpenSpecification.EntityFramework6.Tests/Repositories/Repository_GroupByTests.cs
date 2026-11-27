using System.Data.Entity;
using OpenSpecification.Paging;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class Repository_GroupByTests(TestFactory factory) : IntegrationTest(factory)
{
    private sealed class CountryAggregate
    {
        public string? Name { get; set; }
        public int Count { get; set; }
        public int Sum { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Avg { get; set; }
    }

    private sealed record StoreGroupKey
    {
        public int CompanyId { get; init; }
        public string? City { get; init; }
    }

    private sealed class StoreAggregate
    {
        public int CompanyId { get; set; }
        public string? City { get; set; }
        public int Count { get; set; }
    }

    private sealed class CustomSpecificationEvaluator()
        : SpecificationEvaluator([WhereEvaluator.Instance, SearchEvaluator.Instance, AsNoTrackingEvaluator.Instance]);

    private sealed class CustomCountryRepository(TestDbContext context)
        : RepositoryBase<Country>(context, new CustomSpecificationEvaluator());

    [Fact]
    public async Task SingleKey_AggregatesAndFlags_WorkAcrossRepositoryAndDbSet()
    {
        await SeedCountriesAsync();
        var repo = new Repository<Country>(DbContext);

        var spec = new Specification<Country, CountryAggregate>();
        spec.Query
            .Where(c => c.No >= 10)
            .Search(c => c.Name!, "Grp-%")
            .AsNoTracking()
            .GroupBy(c => c.Name)
            .Select(g => new CountryAggregate
            {
                Name = g.Key,
                Count = g.Count(),
                Sum = g.Sum(c => c.No),
                Min = g.Min(c => c.No),
                Max = g.Max(c => c.No),
                Avg = g.Average(c => c.No),
            });

        var singleSpec = new SingleResultSpecification<Country, CountryAggregate>();
        singleSpec.Query
            .Where(c => c.Name == "Grp-B")
            .GroupBy(c => c.Name)
            .Select(g => new CountryAggregate { Name = g.Key, Count = g.Count(), Sum = g.Sum(c => c.No) });

        var repoList = (await repo.ListAsync(spec)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var dbSetList = (await DbContext.Countries.ToListAsync(spec)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var dbSetEnum = (await DbContext.Countries.ToEnumerableAsync(spec)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var withSpecList = (await DbContext.Countries.WithSpecification(spec).ToListAsync()).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var first = await repo.FirstOrDefaultAsync(singleSpec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);

        repoList.Should().BeEquivalentTo(
        [
            new CountryAggregate { Name = "Grp-A", Count = 2, Sum = 40, Min = 10, Max = 30, Avg = 20 },
            new CountryAggregate { Name = "Grp-B", Count = 2, Sum = 60, Min = 20, Max = 40, Avg = 30 },
        ], o => o.WithStrictOrdering());

        dbSetList.Should().BeEquivalentTo(repoList, o => o.WithStrictOrdering());
        dbSetEnum.Should().BeEquivalentTo(repoList, o => o.WithStrictOrdering());
        withSpecList.Should().BeEquivalentTo(repoList, o => o.WithStrictOrdering());
        first!.Name.Should().Be("Grp-B");
        first.Sum.Should().Be(60);
        single!.Name.Should().Be("Grp-B");
        single.Count.Should().Be(2);
    }

    [Fact]
    public async Task CompositeKeys_AnonymousAndMemberInitializedRecord_TranslateInSql()
    {
        var companyIds = await SeedStoresAsync();
        var repo = new Repository<Store>(DbContext);

        var anonSpec = new Specification<Store, StoreAggregate>();
        anonSpec.Query
            .Include(s => s.Company)
            .Include("Company")
            .GroupBy(s => new { s.CompanyId, s.City })
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var dtoSpec = new Specification<Store, StoreAggregate>();
        dtoSpec.Query
            .GroupBy(s => new StoreGroupKey { CompanyId = s.CompanyId, City = s.City })
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var anonResults = (await repo.ListAsync(anonSpec)).OrderBy(x => x.CompanyId).ThenBy(x => x.City, StringComparer.Ordinal).ToList();
        var dtoResults = (await repo.ListAsync(dtoSpec)).OrderBy(x => x.CompanyId).ThenBy(x => x.City, StringComparer.Ordinal).ToList();

        var expected = new[]
        {
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Austin", Count = 2 },
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Dallas", Count = 1 },
            new StoreAggregate { CompanyId = companyIds.Beta, City = "Austin", Count = 1 },
        };

        anonResults.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
        dtoResults.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task EmptyInput_RegularAndConstantKeys_ReturnZeroGroups()
    {
        var repo = new Repository<Country>(DbContext);

        var regularSpec = new Specification<Country, int>();
        regularSpec.Query.GroupBy(c => c.Name).Select(g => g.Count());

        var constantSpec = new Specification<Country, int>();
        constantSpec.Query.GroupBy(_ => 1).Select(g => g.Count());

        (await repo.ListAsync(regularSpec)).Should().BeEmpty();
        (await repo.ListAsync(constantSpec)).Should().BeEmpty();
    }

    [Fact]
    public async Task PostProcessingAction_RunsOnListReturningMethodsOnly()
    {
        await SeedCountriesAsync();
        var repo = new Repository<Country>(DbContext);

        var spec = new SingleResultSpecification<Country, string?>();
        spec.Query
            .Where(c => c.Name == "Grp-B")
            .GroupBy(c => c.Name)
            .Select(g => g.Key)
            .PostProcessingAction(items => items.Select(x => $"POST:{x}"));

        var list = await repo.ListAsync(spec);
        var dbSetList = await DbContext.Countries.ToListAsync(spec);
        var dbSetEnum = (await DbContext.Countries.ToEnumerableAsync(spec)).ToList();
        var first = await repo.FirstOrDefaultAsync(spec);
        var single = await repo.SingleOrDefaultAsync(spec);

        list.Should().Equal("POST:Grp-B");
        dbSetList.Should().Equal("POST:Grp-B");
        dbSetEnum.Should().Equal("POST:Grp-B");
        first.Should().Be("Grp-B");
        single.Should().Be("Grp-B");
    }

    [Fact]
    public async Task ConflictsAndInterimGuards_ThrowExpectedExceptions()
    {
        var repo = new Repository<Country>(DbContext);

        var missingSelect = new Specification<Country, string?>();
        missingSelect.Query.GroupBy(c => c.Name);

        var mixedSelect = new Specification<Country, string?>();
        mixedSelect.Query.Select(c => c.Name);
        mixedSelect.Query.GroupBy(c => c.Name).Select(g => g.Key);

        var mixedSelectMany = new Specification<Country, string?>();
        mixedSelectMany.Query.GroupBy(c => c.Name);
        mixedSelectMany.Query.SelectMany(c => new[] { c.Name });

        var preOrder = new Specification<Country, string?>();
        preOrder.Query.OrderBy(c => c.Id).GroupBy(c => c.Name).Select(g => g.Key);

        var preSkip = new Specification<Country, string?>();
        preSkip.Query.Skip(1).GroupBy(c => c.Name).Select(g => g.Key);

        var preTake = new Specification<Country, string?>();
        preTake.Query.Take(1).GroupBy(c => c.Name).Select(g => g.Key);

        var distinctGrouped = new Specification<Country, string?>();
        distinctGrouped.Query.Distinct().GroupBy(c => c.Name).Select(g => g.Key);

        var validGrouped = new Specification<Country, string?>();
        validGrouped.Query.GroupBy(c => c.Name).Select(g => g.Key);

        await FluentActions.Awaiting(() => repo.ListAsync(missingSelect)).Should().ThrowAsync<SelectorNotFoundException>();
        await FluentActions.Awaiting(() => repo.ListAsync(mixedSelect)).Should().ThrowAsync<ConcurrentSelectorsException>();
        await FluentActions.Awaiting(() => repo.ListAsync(mixedSelectMany)).Should().ThrowAsync<ConcurrentSelectorsException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preOrder)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preSkip)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preTake)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(distinctGrouped)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.PagedListAsync(validGrouped, new PagingFilter { Page = 1, PageSize = 2 })).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => DbContext.Countries.ToPagedListAsync(validGrouped, new PagingFilter { Page = 1, PageSize = 2 })).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task WithProjectionOf_CustomEvaluator_AndUpcastCountAny_WorkCorrectly()
    {
        await SeedCountriesAsync();
        var repo = new Repository<Country>(DbContext);
        var customRepo = new CustomCountryRepository(DbContext);

        var source = new Specification<Country>();
        source.Query.Where(c => c.No >= 10 && c.Name!.StartsWith("Grp-"));

        var groupedTemplate = new Specification<Country, string?>();
        var groupedBuilder = groupedTemplate.Query.GroupBy(c => c.Name);
        groupedBuilder.Select(g => g.Key);

        var composed = source.WithProjectionOf(groupedTemplate);
        groupedBuilder.Select(g => $"MUTATED:{g.Key}");

        var list = (await repo.ListAsync(composed)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var customList = (await customRepo.ListAsync(composed)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var upcastCount = await repo.CountAsync((ISpecification<Country>)composed);
        var upcastAny = await repo.AnyAsync((ISpecification<Country>)composed);

        list.Should().Equal("Grp-A", "Grp-B");
        customList.Should().Equal("Grp-A", "Grp-B");
        upcastCount.Should().Be(4);
        upcastAny.Should().BeTrue();

        var orderedSource = new Specification<Country>();
        orderedSource.Query.OrderBy(c => c.Id);
        await FluentActions.Awaiting(() => repo.ListAsync(orderedSource.WithProjectionOf(groupedTemplate)))
            .Should().ThrowAsync<NotSupportedException>();
    }

    private async Task SeedCountriesAsync()
    {
        await SeedRangeAsync([
            new Country { Name = "Grp-A", No = 10 },
            new Country { Name = "Grp-A", No = 30 },
            new Country { Name = "Grp-B", No = 20 },
            new Country { Name = "Grp-B", No = 40 },
            new Country { Name = "Other", No = 50 },
        ]);
    }

    private async Task<(int Alpha, int Beta)> SeedStoresAsync()
    {
        var country = new Country { Name = "US", No = 1 };
        await SeedAsync(country);

        var alpha = new Company { Name = "Alpha", CountryId = country.Id };
        var beta = new Company { Name = "Beta", CountryId = country.Id };
        await SeedRangeAsync([alpha, beta]);

        await SeedRangeAsync([
            new Store { Name = "S1", City = "Austin", CompanyId = alpha.Id },
            new Store { Name = "S2", City = "Austin", CompanyId = alpha.Id },
            new Store { Name = "S3", City = "Dallas", CompanyId = alpha.Id },
            new Store { Name = "S4", City = "Austin", CompanyId = beta.Id },
        ]);

        return (alpha.Id, beta.Id);
    }
}

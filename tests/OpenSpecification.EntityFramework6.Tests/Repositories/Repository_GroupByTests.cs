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

    [Fact]
    public async Task HavingAndStronglyTypedPostGroupOrdering_TranslateWithoutBoxingAcrossRepositoryAndDbSet()
    {
        var companyIds = await SeedStoresForHavingAndOrderingAsync();
        var repo = new Repository<Store>(DbContext);

        // Multiple Having predicates (AND semantics), conditional false no-op, and OrderBy -> Having order.
        var havingSpec = new Specification<Store, StoreAggregate>();
        havingSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .Having(g => g.Count() >= 2)
            .Having(g => g.Count() > 99, condition: false)
            .Having(g => g.Sum(s => s.Id) > 0)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.City)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var orderThenHavingSpec = new Specification<Store, StoreAggregate>();
        orderThenHavingSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .Having(g => g.Count() >= 2)
            .Having(g => g.Sum(s => s.Id) > 0)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        // Strongly typed value-type aggregate ordering (Count/Sum/Max/Average) in EF6 without object? boxing.
        var unprojectedAggOrderSpec = new Specification<Store, string>();
        unprojectedAggOrderSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .ThenByDescending(g => g.Max(s => s.Id))
            .Select(g => g.Key.City!);

        var avgOrderSpec = new Specification<Store, string>();
        avgOrderSpec.Query
            .GroupBy(s => s.City)
            .OrderBy(g => g.Average(s => s.CompanyId))
            .ThenBy(g => g.Key)
            .Select(g => g.Key!);

        var sumOrderSpec = new Specification<Store, string>();
        sumOrderSpec.Query
            .GroupBy(s => s.City)
            .OrderByDescending(g => g.Sum(s => s.CompanyId))
            .ThenBy(g => g.Key)
            .Select(g => g.Key!);

        var discardedPrimarySpec = new Specification<Store, string>();
        discardedPrimarySpec.Query
            .GroupBy(s => s.City)
            .OrderBy(g => g.Key, condition: false)
            .ThenByDescending(g => g.Count())
            .OrderByDescending(g => g.Count(), condition: true)
            .ThenBy(g => g.Key)
            .Select(g => g.Key!);

        var nestedDiscardSpec = new Specification<Store, string>();
        var activeHandle = nestedDiscardSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count());
        activeHandle
            .ThenBy(g => g.Key.City, condition: false)
            .ThenByDescending(g => g.Key.CompanyId);
        activeHandle
            .ThenBy(g => g.Key.CompanyId)
            .ThenBy(g => g.Key.City)
            .Select(g => g.Key.City!);

        var emptySpec = new Specification<Store, string>();
        emptySpec.Query
            .Where(s => s.Id < 0)
            .GroupBy(s => s.City)
            .Having(g => g.Count() > 0)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key!);

        var havingList = await repo.ListAsync(havingSpec);
        var orderThenHavingList = await repo.ListAsync(orderThenHavingSpec);
        var aggList = await repo.ListAsync(unprojectedAggOrderSpec);
        var aggDbSetList = await DbContext.Stores.ToListAsync(unprojectedAggOrderSpec);
        var aggDbSetEnum = (await DbContext.Stores.ToEnumerableAsync(unprojectedAggOrderSpec)).ToList();
        var aggWithSpec = await DbContext.Stores.WithSpecification(unprojectedAggOrderSpec).ToListAsync();
        var aggFirst = await repo.FirstOrDefaultAsync(unprojectedAggOrderSpec);
        var avgList = await repo.ListAsync(avgOrderSpec);
        var sumList = await repo.ListAsync(sumOrderSpec);
        var discardedList = await repo.ListAsync(discardedPrimarySpec);
        var nestedDiscardList = await repo.ListAsync(nestedDiscardSpec);
        var emptyList = await repo.ListAsync(emptySpec);

        havingList.Should().BeEquivalentTo(
        [
            new StoreAggregate { CompanyId = companyIds.Beta, City = "Denver", Count = 3 },
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Austin", Count = 2 },
        ], o => o.WithStrictOrdering());
        orderThenHavingList.Should().BeEquivalentTo(havingList, o => o.WithStrictOrdering());

        aggList.Should().Equal("Denver", "Austin", "Dallas", "Austin");
        aggDbSetList.Should().Equal(aggList);
        aggDbSetEnum.Should().Equal(aggList);
        aggWithSpec.Should().Equal(aggList);
        aggFirst.Should().Be("Denver");

        avgList.Should().Equal("Dallas", "Austin", "Denver");
        sumList.Should().Equal("Denver", "Austin", "Dallas");
        discardedList.Should().Equal("Austin", "Denver", "Dallas");
        nestedDiscardList.Should().Equal("Denver", "Austin", "Dallas", "Austin");
        emptyList.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicatePrimaryGroupOrderChain_ThrowsAtEvaluationTime_AndWithProjectionOfClonesHavingAndOrder()
    {
        await SeedCountriesAsync();
        var repo = new Repository<Country>(DbContext);

        var directDup = new Specification<Country, string?>();
        directDup.Query
            .GroupBy(c => c.Name)
            .OrderBy(g => g.Key)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var acrossHavingDup = new Specification<Country, string?>();
        acrossHavingDup.Query
            .GroupBy(c => c.Name)
            .OrderBy(g => g.Key)
            .Having(g => g.Count() > 0)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var oneActiveOneFalse = new Specification<Country, string?>();
        oneActiveOneFalse.Query
            .Where(c => c.Name!.StartsWith("Grp-"))
            .GroupBy(c => c.Name)
            .OrderByDescending(g => g.Key, true)
            .OrderBy(g => g.Count(), false)
            .Select(g => g.Key);

        await FluentActions.Awaiting(() => repo.ListAsync(directDup)).Should().ThrowAsync<DuplicateOrderChainException>();
        await FluentActions.Awaiting(() => DbContext.Countries.ToListAsync(directDup)).Should().ThrowAsync<DuplicateOrderChainException>();
        await FluentActions.Awaiting(() => repo.ListAsync(acrossHavingDup)).Should().ThrowAsync<DuplicateOrderChainException>();
        (await repo.ListAsync(oneActiveOneFalse)).Should().Equal("Grp-B", "Grp-A");

        var source = new Specification<Country>();
        source.Query.Where(c => c.Name!.StartsWith("Grp-"));

        var template = new Specification<Country, string?>();
        var templateGrouped = template.Query
            .GroupBy(c => c.Name)
            .Having(g => g.Count() >= 2);
        templateGrouped.OrderBy(g => g.Key).Select(g => g.Key);

        var composed = source.WithProjectionOf(template);
        templateGrouped.Having(g => g.Sum(c => c.No) >= 60);
        templateGrouped.OrderByDescending(g => g.Sum(c => c.No));

        (await repo.ListAsync(composed)).Should().Equal("Grp-A", "Grp-B");
        await FluentActions.Awaiting(() => repo.ListAsync(template)).Should().ThrowAsync<DuplicateOrderChainException>();
    }

    private async Task<(int Alpha, int Beta)> SeedStoresForHavingAndOrderingAsync()
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
            new Store { Name = "S5", City = "Denver", CompanyId = beta.Id },
            new Store { Name = "S6", City = "Denver", CompanyId = beta.Id },
            new Store { Name = "S7", City = "Denver", CompanyId = beta.Id },
        ]);

        return (alpha.Id, beta.Id);
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

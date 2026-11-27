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

    private sealed class SmallPageStoreRepository(TestDbContext context) : RepositoryBase<Store>(context)
    {
        protected override PaginationSettings PaginationSettings { get; } = new(2, 3);
    }

    private sealed class CompanyScopedStoreRepository(TestDbContext context, int companyId) : RepositoryBase<Store>(context)
    {
        protected override IQueryable<Store> ApplySpecification(ISpecification<Store> specification, bool evaluateCriteriaOnly = false)
            => SpecificationEvaluator.GetQuery(DbContext.Set<Store>().Where(s => s.CompanyId == companyId), specification, evaluateCriteriaOnly);

        protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<Store, TResult> specification)
            => SpecificationEvaluator.GetQuery(DbContext.Set<Store>().Where(s => s.CompanyId == companyId), specification);
    }

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
    public async Task Conflicts_ThrowExpectedExceptions()
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

        await FluentActions.Awaiting(() => repo.ListAsync(missingSelect)).Should().ThrowAsync<SelectorNotFoundException>();
        await FluentActions.Awaiting(() => repo.ListAsync(mixedSelect)).Should().ThrowAsync<ConcurrentSelectorsException>();
        await FluentActions.Awaiting(() => repo.ListAsync(mixedSelectMany)).Should().ThrowAsync<ConcurrentSelectorsException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preOrder)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preSkip)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(preTake)).Should().ThrowAsync<NotSupportedException>();
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

    [Fact]
    public async Task GroupSkipTakeAndPostGroupDistinct_WorkAcrossSurfaces_AndRejectOrderByWithDistinct()
    {
        var companyIds = await SeedStoresForHavingAndOrderingAsync();
        var repo = new Repository<Store>(DbContext);

        var sqlCommands = new List<string>();
        DbContext.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };

        var preSliceSpec = new Specification<Store, StoreAggregate>();
        preSliceSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .ThenBy(g => g.Key.City)
            .Skip(99, false)
            .Take(0, false)
            .Skip(1)
            .Take(2)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var postSliceSpec = new Specification<Store, StoreAggregate>();
        postSliceSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .ThenBy(g => g.Key.City)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() })
            .Skip(1)
            .Take(2);

        var skipZeroSpec = new Specification<Store, string>();
        skipZeroSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .Skip(0)
            .Select(g => g.Key.City!);

        var takeZeroSpec = new Specification<Store, string>();
        takeZeroSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key.City!)
            .Take(0);

        var expectedSlice = new[]
        {
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Austin", Count = 2 },
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Dallas", Count = 1 },
        };

        (await repo.ListAsync(preSliceSpec)).Should().BeEquivalentTo(expectedSlice, o => o.WithStrictOrdering());
        (await repo.ListAsync(postSliceSpec)).Should().BeEquivalentTo(expectedSlice, o => o.WithStrictOrdering());
        (await DbContext.Stores.ToListAsync(preSliceSpec)).Should().BeEquivalentTo(expectedSlice, o => o.WithStrictOrdering());

        sqlCommands.Clear();
        (await repo.ListAsync(skipZeroSpec)).Should().HaveCount(4);
        sqlCommands.Should().OnlyContain(c => !c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase) && !c.Contains("ROW_NUMBER", StringComparison.OrdinalIgnoreCase));
        (await repo.ListAsync(takeZeroSpec)).Should().BeEmpty();

        // Post-group Distinct() and pre-GroupBy Query.Distinct() deduplicate {3, 2, 1, 1} -> {1, 2, 3}.
        var postDistinctSpec = new Specification<Store, int>();
        postDistinctSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .Select(g => g.Count())
            .Distinct();

        var preDistinctSpec = new Specification<Store, int>();
        preDistinctSpec.Query
            .Distinct()
            .GroupBy(s => new { s.CompanyId, s.City })
            .Select(g => g.Count());

        var distinctFalseSpec = new Specification<Store, int>();
        distinctFalseSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .Select(g => g.Count())
            .Distinct(false);

        var distinctThenTakeSpec = new Specification<Store, int>();
        distinctThenTakeSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .Select(g => g.Count())
            .Distinct()
            .Take(4);

        var inactiveOrderDistinctSpec = new Specification<Store, int>();
        inactiveOrderDistinctSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderBy(g => g.Key.City, false)
            .Select(g => g.Count())
            .Distinct();

        (await repo.ListAsync(postDistinctSpec)).OrderBy(x => x).Should().Equal(1, 2, 3);
        (await repo.ListAsync(preDistinctSpec)).OrderBy(x => x).Should().Equal(1, 2, 3);
        (await repo.ListAsync(distinctFalseSpec)).OrderBy(x => x).Should().Equal(1, 1, 2, 3);
        (await repo.ListAsync(distinctThenTakeSpec)).Should().HaveCount(3);
        (await repo.ListAsync(inactiveOrderDistinctSpec)).OrderBy(x => x).Should().Equal(1, 2, 3);

        // Active group OrderBy + Distinct throws NotSupportedException with 0 SQL.
        var orderDistinctPost = new Specification<Store, int>();
        orderDistinctPost.Query.GroupBy(s => s.City).OrderBy(g => g.Key).Select(g => g.Count()).Distinct();

        var orderDistinctPre = new Specification<Store, int>();
        orderDistinctPre.Query.Distinct().GroupBy(s => s.City).OrderByDescending(g => g.Count()).Select(g => g.Count());

        sqlCommands.Clear();
        await FluentActions.Awaiting(() => repo.ListAsync(orderDistinctPost)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => repo.ListAsync(orderDistinctPre)).Should().ThrowAsync<NotSupportedException>();
        sqlCommands.Should().BeEmpty();

        // WithProjectionOf clones group Skip/Take and IsDistinct without state leakage.
        var source = new Specification<Store>();
        source.Query.Where(s => s.Id > 0);

        var sliceTemplate = new Specification<Store, StoreAggregate>();
        var sliceResult = sliceTemplate.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .ThenBy(g => g.Key.City)
            .Skip(1)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var composedSlice = source.WithProjectionOf(sliceTemplate);
        sliceResult.Take(1);

        (await repo.ListAsync(composedSlice)).Should().HaveCount(3);
        (await repo.ListAsync(sliceTemplate)).Should().HaveCount(1);

        var composedDistinct = source.WithProjectionOf(postDistinctSpec);
        composedDistinct.IsDistinct.Should().BeTrue();
        (await repo.ListAsync(composedDistinct)).OrderBy(x => x).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task PagedListAsyncAndToPagedListAsync_PageOrderedGroupedSpecs_AndFailFastForUnorderedOrDistinct()
    {
        var companyIds = await SeedStoresForHavingAndOrderingAsync();
        var repo = new Repository<Store>(DbContext);
        var smallPageRepo = new SmallPageStoreRepository(DbContext);
        var scopedRepo = new CompanyScopedStoreRepository(DbContext, companyIds.Alpha);

        var sqlCommands = new List<string>();
        DbContext.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };

        // EnsurePageable rejects group Skip > 0 or Take >= 0 with 0 SQL; Skip(0) remains pageable.
        var preSkip = new Specification<Store, string>();
        preSkip.Query.GroupBy(s => s.City).OrderBy(g => g.Key).Skip(1).Select(g => g.Key!);
        var postTakeZero = new Specification<Store, string>();
        postTakeZero.Query.GroupBy(s => s.City).OrderBy(g => g.Key).Select(g => g.Key!).Take(0);
        var skipZero = new Specification<Store, string>();
        skipZero.Query.GroupBy(s => s.City).OrderBy(g => g.Key).Skip(0).Select(g => g.Key!);

        sqlCommands.Clear();
        await FluentActions.Awaiting(() => repo.PagedListAsync(preSkip, new PagingFilter { Page = 1, PageSize = 2 }))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("specification");
        await FluentActions.Awaiting(() => DbContext.Stores.ToPagedListAsync(postTakeZero, new PagingFilter { Page = 1, PageSize = 2 }))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("specification");

        // Unordered and Distinct grouped specs fail fast on BOTH page 1 and page 2 with 0 SQL.
        var unorderedSpec = new Specification<Store, string>();
        unorderedSpec.Query.GroupBy(s => s.City).Select(g => g.Key!);

        var inactiveOrderSpec = new Specification<Store, string>();
        inactiveOrderSpec.Query.GroupBy(s => s.City).OrderBy(g => g.Key, false).Select(g => g.Key!);

        var postDistinctSpec = new Specification<Store, int>();
        postDistinctSpec.Query.GroupBy(s => s.City).Select(g => g.Count()).Distinct();

        var preDistinctSpec = new Specification<Store, int>();
        preDistinctSpec.Query.Distinct().GroupBy(s => s.City).Select(g => g.Count());

        foreach (var page in new[] { 1, 2 })
        {
            await FluentActions.Awaiting(() => repo.PagedListAsync(unorderedSpec, new PagingFilter { Page = page, PageSize = 2 }))
                .Should().ThrowAsync<NotSupportedException>();
            await FluentActions.Awaiting(() => DbContext.Stores.ToPagedListAsync(unorderedSpec, new PagingFilter { Page = page, PageSize = 2 }))
                .Should().ThrowAsync<NotSupportedException>();
            await FluentActions.Awaiting(() => repo.PagedListAsync(inactiveOrderSpec, new PagingFilter { Page = page, PageSize = 2 }))
                .Should().ThrowAsync<NotSupportedException>();
            await FluentActions.Awaiting(() => repo.PagedListAsync(postDistinctSpec, new PagingFilter { Page = page, PageSize = 2 }))
                .Should().ThrowAsync<NotSupportedException>();
            await FluentActions.Awaiting(() => DbContext.Stores.ToPagedListAsync(preDistinctSpec, new PagingFilter { Page = page, PageSize = 2 }))
                .Should().ThrowAsync<NotSupportedException>();
        }
        sqlCommands.Should().BeEmpty();

        var skipZeroPage = await repo.PagedListAsync(skipZero, new PagingFilter { Page = 1, PageSize = 2 });
        skipZeroPage.Pagination.TotalItems.Should().Be(3);
        skipZeroPage.Items.Should().Equal("Austin", "Dallas");

        // Ordered, non-Distinct grouped spec on page 1, page 2, clamped page 99, ignoring PostProcessingAction.
        var orderedSpec = new Specification<Store, StoreAggregate>();
        orderedSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyId)
            .ThenBy(g => g.Key.City)
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() })
            .PostProcessingAction(items => items.Reverse());

        var p1 = await repo.PagedListAsync(orderedSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var p2 = await repo.PagedListAsync(orderedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pClamped = await repo.PagedListAsync(orderedSpec, new PagingFilter { Page = 99, PageSize = 2 });
        var p2DbSet = await DbContext.Stores.ToPagedListAsync(orderedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pSmall = await smallPageRepo.PagedListAsync(orderedSpec, new PagingFilter { Page = 1 });
        var pScoped1 = await scopedRepo.PagedListAsync(orderedSpec, new PagingFilter { Page = 1, PageSize = 1 });
        var pScoped2 = await scopedRepo.PagedListAsync(orderedSpec, new PagingFilter { Page = 2, PageSize = 1 });

        p1.Items.Should().BeEquivalentTo(
        [
            new StoreAggregate { CompanyId = companyIds.Beta, City = "Denver", Count = 3 },
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Austin", Count = 2 },
        ], o => o.WithStrictOrdering());
        p1.Pagination.Should().BeEquivalentTo(new Pagination(1, 2, 4, 2, false, true));

        p2.Items.Should().BeEquivalentTo(
        [
            new StoreAggregate { CompanyId = companyIds.Alpha, City = "Dallas", Count = 1 },
            new StoreAggregate { CompanyId = companyIds.Beta, City = "Austin", Count = 1 },
        ], o => o.WithStrictOrdering());
        p2.Pagination.Should().BeEquivalentTo(new Pagination(2, 2, 4, 2, true, false));
        pClamped.Items.Should().BeEquivalentTo(p2.Items, o => o.WithStrictOrdering());
        pClamped.Pagination.Page.Should().Be(2);
        p2DbSet.Items.Should().BeEquivalentTo(p2.Items, o => o.WithStrictOrdering());
        p2DbSet.Pagination.TotalItems.Should().Be(4);
        pSmall.Pagination.Should().BeEquivalentTo(new Pagination(1, 2, 4, 2, false, true));

        pScoped1.Items.Should().ContainSingle().Which.City.Should().Be("Austin");
        pScoped1.Pagination.Should().BeEquivalentTo(new Pagination(1, 1, 2, 2, false, true));
        pScoped2.Items.Should().ContainSingle().Which.City.Should().Be("Dallas");
        pScoped2.Pagination.Should().BeEquivalentTo(new Pagination(2, 1, 2, 2, true, false));

        // Having-filtered grouped spec (2 surviving groups).
        var havingSpec = new Specification<Store, StoreAggregate>();
        havingSpec.Query
            .GroupBy(s => new { s.CompanyId, s.City })
            .Having(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var pHaving = await repo.PagedListAsync(havingSpec, new PagingFilter { Page = 2, PageSize = 1 });
        pHaving.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new StoreAggregate { CompanyId = companyIds.Alpha, City = "Austin", Count = 2 });
        pHaving.Pagination.Should().BeEquivalentTo(new Pagination(2, 1, 2, 2, true, false));

        // Search-filtered grouped spec (cities starting with "D": Beta/Denver Count=3, Alpha/Dallas Count=1).
        var searchPagedSpec = new Specification<Store, StoreAggregate>();
        searchPagedSpec.Query
            .Search(s => s.City!, "D%")
            .GroupBy(s => new { s.CompanyId, s.City })
            .OrderByDescending(g => g.Count())
            .Select(g => new StoreAggregate { CompanyId = g.Key.CompanyId, City = g.Key.City, Count = g.Count() });

        var pSearch = await repo.PagedListAsync(searchPagedSpec, new PagingFilter { Page = 2, PageSize = 1 });
        pSearch.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new StoreAggregate { CompanyId = companyIds.Alpha, City = "Dallas", Count = 1 });
        pSearch.Pagination.Should().BeEquivalentTo(new Pagination(2, 1, 2, 2, true, false));

        // Empty result grouped spec.
        var emptySpec = new Specification<Store, string>();
        emptySpec.Query.Where(s => s.Id < 0).GroupBy(s => s.City).OrderBy(g => g.Key).Select(g => g.Key!);
        var pEmpty = await repo.PagedListAsync(emptySpec, new PagingFilter { Page = 2, PageSize = 2 });
        pEmpty.Items.Should().BeEmpty();
        pEmpty.Pagination.Should().BeEquivalentTo(new Pagination(1, 2, 0, 1, false, false));
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

// Drives #99, #100, and #101's GroupBy support through the EF Core provider and In-Memory evaluator:
// single-key and composite-key grouped projections, Having, strongly typed OrderBy/ThenBy,
// group Skip/Take, post-group Distinct, grouped PagedListAsync/ToPagedListAsync,
// PostProcessingAction (list-only), EnableCache/WithCacheKey, builder/evaluator conflict validation,
// WithProjectionOf cloning, custom evaluator inheritance, IsSatisfiedBy/upcast criteria-only behavior,
// and 10.x contract checks.
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using OpenSpecification.Paging;

const string Catalog = "VerifySpecificationGroupByEFCore";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var sqlLog = new SqlCaptureInterceptor();
var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();
var options = new DbContextOptionsBuilder<GroupByContext>()
    .UseSqlServer(connectionString)
    .AddInterceptors(sqlLog)
    .Options;

await using (var db = new GroupByContext(options))
{
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();

    var alpha = new Company { Name = "Alpha" };
    var beta = new Company { Name = "Beta" };
    db.Companies.AddRange(alpha, beta);
    await db.SaveChangesAsync();

    db.Products.AddRange(
        new Product { Name = "P01", Category = "Cat-1", Price = 10, CompanyId = alpha.Id },
        new Product { Name = "P02", Category = "Cat-1", Price = 30, CompanyId = alpha.Id },
        new Product { Name = "P03", Category = "Cat-2", Price = 20, CompanyId = alpha.Id },
        new Product { Name = "P04", Category = "Cat-2", Price = 40, CompanyId = alpha.Id },
        new Product { Name = "P05", Category = "Cat-1", Price = 50, CompanyId = beta.Id },
        new Product { Name = "P06", Category = "Cat-2", Price = 60, CompanyId = beta.Id },
        new Product { Name = "P07", Category = "Cat-2", Price = 80, CompanyId = beta.Id },
        new Product { Name = "P08", Category = "Cat-1", Price = 100, CompanyId = alpha.Id, IsDeleted = true }
    );
    await db.SaveChangesAsync();
}

try
{
    await GroupByDriver.Run(options, sqlLog);
}
finally
{
    SqlConnection.ClearAllPools();
    await using var db = new GroupByContext(options);
    await db.Database.EnsureDeletedAsync();
}

return 0;

static class GroupByDriver
{
    public static async Task Run(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        RunContractSurface();
        await RunSingleAndCompositeGroups(options, sqlLog);
        await RunHavingAndOrdering(options, sqlLog);
        await RunSkipTakeDistinctAndPaging(options, sqlLog);
        await RunPostProcessingAndCache(options);
        await RunValidationAndConflicts(options, sqlLog);
        await RunCompositionAndCustomEvaluators(options, sqlLog);
    }

    private static void RunContractSurface()
    {
        var groupedPublic = typeof(IGroupedSpecificationBuilder<,,>).IsPublic;
        var orderedPublic = typeof(IOrderedGroupedSpecificationBuilder<,,>).IsPublic;
        var orderedInheritsGrouped = typeof(IGroupedSpecificationBuilder<Product, string, string>).IsAssignableFrom(typeof(IOrderedGroupedSpecificationBuilder<Product, string, string>));
        var groupedResultPublic = typeof(IGroupedResultSpecificationBuilder<,>).IsPublic;
        var dupExPublic = typeof(DuplicateGroupByException).IsPublic;
        var isolated = !typeof(ISpecificationBuilder<Product, string>).IsAssignableFrom(typeof(IGroupedSpecificationBuilder<Product, string, string>))
            && !typeof(ISpecificationBuilder<Product, string>).IsAssignableFrom(typeof(IOrderedGroupedSpecificationBuilder<Product, string, string>))
            && !typeof(ISpecificationBuilder<Product, string>).IsAssignableFrom(typeof(IGroupedResultSpecificationBuilder<Product, string>));
        var ifacesUntouched = typeof(ISpecification<Product>).GetMembers().All(m => !m.Name.Contains("GroupBy", StringComparison.OrdinalIgnoreCase))
            && typeof(ISpecification<Product, string>).GetMembers().All(m => !m.Name.Contains("GroupBy", StringComparison.OrdinalIgnoreCase))
            && typeof(IReadRepositoryBase<Product>).GetMembers().All(m => !m.Name.Contains("GroupBy", StringComparison.OrdinalIgnoreCase));
        var selectVoid = typeof(SpecificationBuilderExtensions)
            .GetMethods()
            .Where(m => m.Name is "Select" or "SelectMany" && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ISpecificationBuilder<,>))
            .All(m => m.ReturnType == typeof(void));

        Console.WriteLine($"contract-surface: grouped={groupedPublic}; ordered={orderedPublic}/{orderedInheritsGrouped}; result={groupedResultPublic}; dupEx={dupExPublic}; isolated={isolated}; ifacesUntouched={ifacesUntouched}; selectVoid={selectVoid}");
    }

    private static async Task RunSingleAndCompositeGroups(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        var allCatSpec = new Specification<Product, CategorySummary>();
        allCatSpec.Query
            .Search(p => p.Category, "Cat-%")
            .AsNoTracking()
            .AsSplitQuery()
            .IgnoreAutoIncludes()
            .TagWith("groupby-efcore-tag")
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary
            {
                Category = g.Key,
                Count = g.Count(),
                Sum = g.Sum(p => p.Price),
                Min = g.Min(p => p.Price),
                Max = g.Max(p => p.Price),
                Avg = g.Average(p => p.Price),
            });

        var filteredWithFlagsSpec = new Specification<Product, CategorySummary>();
        filteredWithFlagsSpec.Query
            .Where(p => p.Company!.Name == "Alpha")
            .IgnoreQueryFilters()
            .AsNoTrackingWithIdentityResolution()
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary
            {
                Category = g.Key,
                Count = g.Count(),
                Sum = g.Sum(p => p.Price),
                Min = g.Min(p => p.Price),
                Max = g.Max(p => p.Price),
                Avg = g.Average(p => p.Price),
            });

        var trackingSpec = new Specification<Product, CategorySummary>();
        trackingSpec.Query
            .Where(p => p.Company!.Name == "Beta")
            .AsTracking()
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary
            {
                Category = g.Key,
                Count = g.Count(),
                Sum = g.Sum(p => p.Price),
            });

        var singleGroupSpec = new SingleResultSpecification<Product, CategorySummary>();
        singleGroupSpec.Query
            .Where(p => p.Company!.Name == "Beta" && p.Category == "Cat-1")
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary { Category = g.Key, Count = g.Count(), Sum = g.Sum(p => p.Price) });

        var anonCompositeSpec = new Specification<Product, CompositeSummary>();
        anonCompositeSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => new CompositeSummary { CompanyName = g.Key.CompanyName, Category = g.Key.Category, Count = g.Count(), Sum = g.Sum(p => p.Price) });

        var dtoCompositeSpec = new Specification<Product, CompositeSummary>();
        dtoCompositeSpec.Query
            .GroupBy(p => new CompositeKey { CompanyName = p.Company!.Name, Category = p.Category })
            .Select(g => new CompositeSummary { CompanyName = g.Key.CompanyName, Category = g.Key.Category, Count = g.Count(), Sum = g.Sum(p => p.Price) });

        var emptyKeySpec = new Specification<Product, CategorySummary>();
        emptyKeySpec.Query
            .Where(p => p.Id < 0)
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary { Category = g.Key, Count = g.Count() });

        var emptyConstSpec = new Specification<Product, int>();
        emptyConstSpec.Query
            .Where(p => p.Id < 0)
            .GroupBy(_ => 1)
            .Select(g => g.Count());

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var factoryRepo = new ProductFactoryRepository(new SimpleFactory(options));

        sqlLog.Clear();
        var repoList = (await repo.ListAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var sqlHasTag = sqlLog.Commands.Any(c => c.Contains("groupby-efcore-tag", StringComparison.Ordinal));
        var dbSetList = (await db.Products.ToListAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var dbSetEnum = (await db.Products.ToEnumerableAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var withSpecList = (await db.Products.WithSpecification(allCatSpec).ToListAsync()).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var first = await repo.FirstOrDefaultAsync(singleGroupSpec);
        var single = await repo.SingleOrDefaultAsync(singleGroupSpec);

        var repoStream = new List<CategorySummary>();
        await foreach (var item in repo.AsAsyncEnumerable(allCatSpec)) repoStream.Add(item);
        var factoryStream = new List<CategorySummary>();
        await foreach (var item in factoryRepo.AsAsyncEnumerable(allCatSpec)) factoryStream.Add(item);
        var dbSetStream = new List<CategorySummary>();
        await foreach (var item in db.Products.AsAsyncEnumerable(allCatSpec)) dbSetStream.Add(item);

        var ignoredFilterList = (await repo.ListAsync(filteredWithFlagsSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var trackingList = (await repo.ListAsync(trackingSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var anonList = (await repo.ListAsync(anonCompositeSpec)).OrderBy(x => x.CompanyName, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ToList();
        var dtoList = (await repo.ListAsync(dtoCompositeSpec)).OrderBy(x => x.CompanyName, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ToList();

        var emptyKeyCount = (await repo.ListAsync(emptyKeySpec)).Count;
        var emptyConstCount = (await repo.ListAsync(emptyConstSpec)).Count;

        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();
        var memAll = allCatSpec.Evaluate(activeEntities).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var memAnon = anonCompositeSpec.Evaluate(activeEntities).OrderBy(x => x.CompanyName, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ToList();
        var memDto = dtoCompositeSpec.Evaluate(activeEntities).OrderBy(x => x.CompanyName, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal).ToList();
        var memEmptyKey = emptyKeySpec.Evaluate(activeEntities).Count();
        var memEmptyConst = emptyConstSpec.Evaluate(activeEntities).Count();

        var singleStr = string.Join(",", repoList.Select(FormatCat));
        var surfacesMatch = singleStr == string.Join(",", dbSetList.Select(FormatCat))
            && singleStr == string.Join(",", dbSetEnum.Select(FormatCat))
            && singleStr == string.Join(",", withSpecList.Select(FormatCat))
            && singleStr == string.Join(",", repoStream.OrderBy(x => x.Category, StringComparer.Ordinal).Select(FormatCat))
            && singleStr == string.Join(",", factoryStream.OrderBy(x => x.Category, StringComparer.Ordinal).Select(FormatCat))
            && singleStr == string.Join(",", dbSetStream.OrderBy(x => x.Category, StringComparer.Ordinal).Select(FormatCat))
            && singleStr == string.Join(",", memAll.Select(FormatCat));

        var anonStr = string.Join(",", anonList.Select(FormatComp));
        var dtoStr = string.Join(",", dtoList.Select(FormatComp));
        var compositeMatch = anonStr == dtoStr
            && anonStr == string.Join(",", memAnon.Select(FormatComp))
            && anonStr == string.Join(",", memDto.Select(FormatComp));

        Console.WriteLine($"groupby-single-and-composite: single=[{singleStr}]; surfacesMatch={surfacesMatch}; tag={sqlHasTag}; ignoreFilter=[{string.Join(",", ignoredFilterList.Select(x => $"{x.Category}:{x.Count}/{x.Sum}"))}]; tracking=[{string.Join(",", trackingList.Select(x => $"{x.Category}:{x.Count}/{x.Sum}"))}]; first={first?.Category}:{first?.Sum}; singleRes={single?.Category}:{single?.Sum}; composite=[{anonStr}]; compositeMatch={compositeMatch}; empty={emptyKeyCount}/{emptyConstCount}/{memEmptyKey}/{memEmptyConst}");
    }

    private static async Task RunHavingAndOrdering(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        var havingAndSpec = new Specification<Product, CompositeSummary>();
        havingAndSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Having(g => g.Count() >= 2)
            .Having(g => g.Count() > 99, condition: false)
            .Having(g => g.Sum(p => p.Price) >= 50)
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Select(g => new CompositeSummary
            {
                CompanyName = g.Key.CompanyName,
                Category = g.Key.Category,
                Count = g.Count(),
                Sum = g.Sum(p => p.Price),
            });

        var orderThenHavingSpec = new Specification<Product, CompositeSummary>();
        orderThenHavingSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Having(g => g.Count() >= 2)
            .Having(g => g.Sum(p => p.Price) >= 50)
            .Select(g => new CompositeSummary
            {
                CompanyName = g.Key.CompanyName,
                Category = g.Key.Category,
                Count = g.Count(),
                Sum = g.Sum(p => p.Price),
            });

        var unprojectedAggOrderSpec = new Specification<Product, string>();
        unprojectedAggOrderSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyName)
            .ThenByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var avgOrderSpec = new Specification<Product, string>();
        avgOrderSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderBy(g => g.Average(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var maxOrderSpec = new Specification<Product, string>();
        maxOrderSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Max(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var discardedPrimarySpec = new Specification<Product, string>();
        discardedPrimarySpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderBy(g => g.Key.Category, condition: false)
            .ThenByDescending(g => g.Sum(p => p.Price))
            .OrderByDescending(g => g.Sum(p => p.Price), condition: true)
            .ThenBy(g => g.Key.CompanyName)
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var nestedDiscardAndSavedHandleSpec = new Specification<Product, string>();
        var activeOrdHandle = nestedDiscardAndSavedHandleSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Count());
        activeOrdHandle
            .ThenBy(g => g.Key.CompanyName, condition: false)
            .ThenByDescending(g => g.Sum(p => p.Price));
        activeOrdHandle
            .ThenBy(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var emptyHavingOrderSpec = new Specification<Product, string>();
        emptyHavingOrderSpec.Query
            .Where(p => p.Id < 0)
            .GroupBy(p => p.Category)
            .Having(g => g.Count() > 0)
            .OrderByDescending(g => g.Sum(p => p.Price))
            .ThenBy(g => g.Key)
            .Select(g => g.Key);

        var dupOrderDirectSpec = new Specification<Product, string>();
        dupOrderDirectSpec.Query
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var dupOrderAcrossHavingSpec = new Specification<Product, string>();
        dupOrderAcrossHavingSpec.Query
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Having(g => g.Count() > 0)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var oneActiveOneFalseOrderSpec = new Specification<Product, string>();
        oneActiveOneFalseOrderSpec.Query
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key, condition: true)
            .OrderByDescending(g => g.Count(), condition: false)
            .Select(g => g.Key);

        var nullGuardsOk = Throws<ArgumentNullException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Having(null!);
        }) && Throws<ArgumentNullException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).OrderBy<Product, string, string, string>(null!);
        });
        var inactiveNullSpec = new Specification<Product, string>();
        inactiveNullSpec.Query
            .GroupBy(p => p.Category)
            .Having(null!, condition: false)
            .OrderBy<Product, string, string, string>(null!, condition: false)
            .Select(g => g.Key);

        var cloneSource = new Specification<Product>();
        var cloneTemplate = new Specification<Product, string>();
        var templateGrouped = cloneTemplate.Query
            .GroupBy(p => p.Category)
            .Having(g => g.Count() >= 1);
        templateGrouped.OrderBy(g => g.Key).Select(g => g.Key);

        var clonedSpec = cloneSource.WithProjectionOf(cloneTemplate);
        templateGrouped.Having(g => g.Count() >= 4);
        templateGrouped.OrderByDescending(g => g.Count());

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var factoryRepo = new ProductFactoryRepository(new SimpleFactory(options));
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var havingList = await repo.ListAsync(havingAndSpec);
        var orderThenHavingList = await repo.ListAsync(orderThenHavingSpec);
        var havingMem = havingAndSpec.Evaluate(activeEntities).ToList();

        var aggList = await repo.ListAsync(unprojectedAggOrderSpec);
        var aggDbSetList = await db.Products.ToListAsync(unprojectedAggOrderSpec);
        var aggDbSetEnum = (await db.Products.ToEnumerableAsync(unprojectedAggOrderSpec)).ToList();
        var aggWithSpec = await db.Products.WithSpecification(unprojectedAggOrderSpec).ToListAsync();
        var aggFirst = await repo.FirstOrDefaultAsync(unprojectedAggOrderSpec);
        var aggStream = new List<string>();
        await foreach (var item in repo.AsAsyncEnumerable(unprojectedAggOrderSpec)) aggStream.Add(item);
        var aggFactoryStream = new List<string>();
        await foreach (var item in factoryRepo.AsAsyncEnumerable(unprojectedAggOrderSpec)) aggFactoryStream.Add(item);
        var aggDbSetStream = new List<string>();
        await foreach (var item in db.Products.AsAsyncEnumerable(unprojectedAggOrderSpec)) aggDbSetStream.Add(item);
        var aggMem = unprojectedAggOrderSpec.Evaluate(activeEntities).ToList();

        var avgList = await repo.ListAsync(avgOrderSpec);
        var avgMem = avgOrderSpec.Evaluate(activeEntities).ToList();
        var maxList = await repo.ListAsync(maxOrderSpec);
        var maxMem = maxOrderSpec.Evaluate(activeEntities).ToList();

        var discardedPrimaryList = await repo.ListAsync(discardedPrimarySpec);
        var discardedPrimaryMem = discardedPrimarySpec.Evaluate(activeEntities).ToList();
        var nestedDiscardList = await repo.ListAsync(nestedDiscardAndSavedHandleSpec);
        var nestedDiscardMem = nestedDiscardAndSavedHandleSpec.Evaluate(activeEntities).ToList();
        var emptyCountSql = (await repo.ListAsync(emptyHavingOrderSpec)).Count;
        var emptyCountMem = emptyHavingOrderSpec.Evaluate(activeEntities).Count();
        var oneActiveOrderList = await repo.ListAsync(oneActiveOneFalseOrderSpec);
        var inactiveNullCount = (await repo.ListAsync(inactiveNullSpec)).Count;

        sqlLog.Clear();
        var dupOrderOk = Throws<DuplicateOrderChainException>(() => dupOrderDirectSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<DuplicateOrderChainException>(() => repo.ListAsync(dupOrderDirectSpec))
            && await ThrowsAsync<DuplicateOrderChainException>(() => db.Products.ToListAsync(dupOrderDirectSpec))
            && Throws<DuplicateOrderChainException>(() => dupOrderAcrossHavingSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<DuplicateOrderChainException>(() => repo.ListAsync(dupOrderAcrossHavingSpec));
        var dupOrderSql = sqlLog.Commands.Count;

        var clonedSql = await repo.ListAsync(clonedSpec);
        var clonedMem = clonedSpec.Evaluate(activeEntities).ToList();
        var templateNowDup = Throws<DuplicateOrderChainException>(() => cloneTemplate.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<DuplicateOrderChainException>(() => repo.ListAsync(cloneTemplate));

        var havingStr = string.Join(",", havingList.Select(FormatComp));
        var havingMatch = havingStr == string.Join(",", orderThenHavingList.Select(FormatComp))
            && havingStr == string.Join(",", havingMem.Select(FormatComp));
        var aggStr = string.Join(",", aggList);
        var aggSurfacesMatch = aggStr == string.Join(",", aggDbSetList)
            && aggStr == string.Join(",", aggDbSetEnum)
            && aggStr == string.Join(",", aggWithSpec)
            && aggStr == string.Join(",", aggStream)
            && aggStr == string.Join(",", aggFactoryStream)
            && aggStr == string.Join(",", aggDbSetStream)
            && aggStr == string.Join(",", aggMem)
            && aggFirst == "Alpha/Cat-2";
        var avgMaxMatch = avgList.SequenceEqual(avgMem) && maxList.SequenceEqual(maxMem);
        var chainIsolationMatch = discardedPrimaryList.SequenceEqual(discardedPrimaryMem)
            && nestedDiscardList.SequenceEqual(nestedDiscardMem);
        var cloneNoLeak = clonedSql.SequenceEqual(["Cat-1", "Cat-2"])
            && clonedMem.SequenceEqual(["Cat-1", "Cat-2"])
            && templateNowDup;

        Console.WriteLine($"having-and-ordering: having=[{havingStr}]; havingMatch={havingMatch}; aggOrder=[{aggStr}]; aggSurfacesMatch={aggSurfacesMatch}; avg=[{string.Join(",", avgList)}]; max=[{string.Join(",", maxList)}]; avgMaxMatch={avgMaxMatch}; discardedPrimary=[{string.Join(",", discardedPrimaryList)}]; nestedDiscard=[{string.Join(",", nestedDiscardList)}]; chainIsolationMatch={chainIsolationMatch}; empty={emptyCountSql}/{emptyCountMem}; dupOrder={dupOrderOk}/sql={dupOrderSql}; oneActive=[{string.Join(",", oneActiveOrderList)}]; nullGuards={nullGuardsOk}/inactiveCount={inactiveNullCount}; cloneNoLeak={cloneNoLeak}");
    }

    private static async Task RunSkipTakeDistinctAndPaging(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        var preSelectSliceSpec = new Specification<Product, string>();
        preSelectSliceSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Skip(99, condition: false)
            .Take(0, condition: false)
            .Skip(1)
            .Take(2)
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var postSelectSliceSpec = new Specification<Product, string>();
        postSelectSliceSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category)
            .Skip(99, condition: false)
            .Take(0, condition: false)
            .Skip(1)
            .Take(2);

        var skipZeroSpec = new Specification<Product, string>();
        skipZeroSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Skip(0)
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var takeZeroSpec = new Specification<Product, string>();
        takeZeroSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category)
            .Take(0);

        var dupSliceGuardsOk = Throws<DuplicateSkipException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Skip(1).Skip(2);
        }) && Throws<DuplicateSkipException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Skip(1).Select(g => g.Key).Skip(2);
        }) && Throws<DuplicateSkipException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).Skip(1).Skip(2);
        }) && Throws<DuplicateTakeException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Take(2).Take(1);
        }) && Throws<DuplicateTakeException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Take(2).Select(g => g.Key).Take(1);
        }) && Throws<DuplicateTakeException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).Take(2).Take(1);
        });

        var nullGuardsOk = Throws<ArgumentNullException>(() => ((IGroupedSpecificationBuilder<Product, string, string>)null!).Skip(1))
            && Throws<ArgumentNullException>(() => ((IGroupedSpecificationBuilder<Product, string, string>)null!).Take(1))
            && Throws<ArgumentNullException>(() => ((IGroupedResultSpecificationBuilder<Product, string>)null!).Skip(1))
            && Throws<ArgumentNullException>(() => ((IGroupedResultSpecificationBuilder<Product, string>)null!).Take(1))
            && Throws<ArgumentNullException>(() => ((IGroupedResultSpecificationBuilder<Product, string>)null!).Distinct());

        var postDistinctSpec = new Specification<Product, int>();
        postDistinctSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count())
            .Distinct();

        var preDistinctSpec = new Specification<Product, int>();
        preDistinctSpec.Query
            .Distinct()
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count());

        var distinctFalseSpec = new Specification<Product, int>();
        distinctFalseSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count())
            .Distinct(condition: false);

        var distinctBeforeTakeSpec = new Specification<Product, int>();
        distinctBeforeTakeSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count())
            .Distinct()
            .Take(3);

        var distinctBeforeSkipTakeSpec = new Specification<Product, int>();
        distinctBeforeSkipTakeSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count())
            .Distinct()
            .Skip(1)
            .Take(1);

        var orderDistinctPostSpec = new Specification<Product, int>();
        orderDistinctPostSpec.Query
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Select(g => g.Count())
            .Distinct();

        var orderDistinctPreSpec = new Specification<Product, int>();
        orderDistinctPreSpec.Query
            .Distinct()
            .GroupBy(p => p.Category)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Count());

        var inactiveOrderDistinctSpec = new Specification<Product, int>();
        inactiveOrderDistinctSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderBy(g => g.Key.Category, condition: false)
            .Select(g => g.Count())
            .Distinct();

        var orderedPagedSpec = new Specification<Product, string>();
        orderedPagedSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category)
            .PostProcessingAction(items => items.Reverse());

        var havingPagedSpec = new Specification<Product, string>();
        havingPagedSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Having(g => g.Sum(p => p.Price) >= 50)
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var emptyPagedSpec = new Specification<Product, string>();
        emptyPagedSpec.Query
            .Where(p => p.Id < 0)
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Select(g => g.Key);

        var unorderedPagedSpec = new Specification<Product, string>();
        unorderedPagedSpec.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var cloneSource = new Specification<Product>();
        var cloneSliceTemplate = new Specification<Product, string>();
        var cloneSliceResult = cloneSliceTemplate.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .OrderByDescending(g => g.Sum(p => p.Price))
            .Skip(1)
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);
        var clonedSlice = cloneSource.WithProjectionOf(cloneSliceTemplate);
        cloneSliceResult.Take(1);

        var cloneDistinctTemplate = new Specification<Product, int>();
        cloneDistinctTemplate.Query
            .GroupBy(p => new { CompanyName = p.Company!.Name, p.Category })
            .Select(g => g.Count())
            .Distinct();
        var clonedDistinct = cloneSource.WithProjectionOf(cloneDistinctTemplate);

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var factoryRepo = new ProductFactoryRepository(new SimpleFactory(options));
        var tenantAlphaRepo = new TenantAlphaProductRepository(db);
        var smallPageRepo = new SmallPageProductRepository(db);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var preSliceSql = await repo.ListAsync(preSelectSliceSpec);
        var postSliceSql = await repo.ListAsync(postSelectSliceSpec);
        var preSliceMem = preSelectSliceSpec.Evaluate(activeEntities).ToList();
        var postSliceMem = postSelectSliceSpec.Evaluate(activeEntities).ToList();
        var sliceMatch = preSliceSql.SequenceEqual(["Alpha/Cat-2", "Beta/Cat-1"])
            && postSliceSql.SequenceEqual(preSliceSql)
            && preSliceMem.SequenceEqual(preSliceSql)
            && postSliceMem.SequenceEqual(preSliceSql);

        sqlLog.Clear();
        var skipZeroSql = await repo.ListAsync(skipZeroSpec);
        var skipZeroNoOffset = sqlLog.Commands.All(c => !c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        var skipZeroOk = skipZeroSql.Count == 4 && skipZeroNoOffset;
        var takeZeroOk = (await repo.ListAsync(takeZeroSpec)).Count == 0 && !takeZeroSpec.Evaluate(activeEntities).Any();

        var postDistinctSql = (await repo.ListAsync(postDistinctSpec)).OrderBy(x => x).ToList();
        var preDistinctSql = (await repo.ListAsync(preDistinctSpec)).OrderBy(x => x).ToList();
        var postDistinctMem = postDistinctSpec.Evaluate(activeEntities).OrderBy(x => x).ToList();
        var preDistinctMem = preDistinctSpec.Evaluate(activeEntities).OrderBy(x => x).ToList();
        var distinctFalseCount = (await repo.ListAsync(distinctFalseSpec)).Count;
        var distinctTakeSql = (await repo.ListAsync(distinctBeforeTakeSpec)).Count;
        var distinctTakeMem = distinctBeforeTakeSpec.Evaluate(activeEntities).Count();
        var distinctSkipTakeSql = (await repo.ListAsync(distinctBeforeSkipTakeSpec)).Count;
        var distinctSkipTakeMem = distinctBeforeSkipTakeSpec.Evaluate(activeEntities).Count();
        var inactiveOrderDistinctSql = (await repo.ListAsync(inactiveOrderDistinctSpec)).OrderBy(x => x).ToList();
        var distinctOk = postDistinctSpec.IsDistinct
            && !distinctFalseSpec.IsDistinct
            && postDistinctSql.SequenceEqual([1, 2])
            && preDistinctSql.SequenceEqual([1, 2])
            && postDistinctMem.SequenceEqual([1, 2])
            && preDistinctMem.SequenceEqual([1, 2])
            && distinctFalseCount == 4
            && distinctTakeSql == 2 && distinctTakeMem == 2
            && distinctSkipTakeSql == 1 && distinctSkipTakeMem == 1
            && inactiveOrderDistinctSql.SequenceEqual([1, 2]);

        sqlLog.Clear();
        var orderDistinctBlocked = Throws<NotSupportedException>(() => orderDistinctPostSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(orderDistinctPostSpec))
            && await ThrowsAsync<NotSupportedException>(() => db.Products.ToListAsync(orderDistinctPostSpec))
            && Throws<NotSupportedException>(() => orderDistinctPreSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(orderDistinctPreSpec));
        var ensurePageableOk = await ThrowsArgSpecAsync(() => repo.PagedListAsync(preSelectSliceSpec, new PagingFilter { Page = 1, PageSize = 2 }))
            && await ThrowsArgSpecAsync(() => factoryRepo.PagedListAsync(postSelectSliceSpec, new PagingFilter { Page = 1, PageSize = 2 }))
            && await ThrowsArgSpecAsync(() => db.Products.ToPagedListAsync(takeZeroSpec, new PagingFilter { Page = 1, PageSize = 2 }));
        var blockedSqlCount = sqlLog.Commands.Count;

        var skipZeroPaged = await repo.PagedListAsync(skipZeroSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var skipZeroPageableOk = skipZeroPaged.Pagination.TotalItems == 4 && skipZeroPaged.Items.Count == 2;

        var p1 = await repo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var p2 = await repo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pClamped = await repo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 99, PageSize = 2 });
        var p2Factory = await factoryRepo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var p2DbSet = await db.Products.ToPagedListAsync(orderedPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pHaving = await repo.PagedListAsync(havingPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pEmpty = await repo.PagedListAsync(emptyPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pTenant1 = await tenantAlphaRepo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 1, PageSize = 1 });
        var pTenant2 = await tenantAlphaRepo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 2, PageSize = 1 });
        var pSmall = await smallPageRepo.PagedListAsync(orderedPagedSpec, new PagingFilter { Page = 1 });

        var orderedPagingOk = p1.Items.SequenceEqual(["Beta/Cat-2", "Alpha/Cat-2"])
            && p1.Pagination is { Page: 1, PageSize: 2, TotalItems: 4, TotalPages: 2, HasPrevious: false, HasNext: true }
            && p2.Items.SequenceEqual(["Beta/Cat-1", "Alpha/Cat-1"])
            && p2.Pagination is { Page: 2, PageSize: 2, TotalItems: 4, TotalPages: 2, HasPrevious: true, HasNext: false }
            && pClamped.Items.SequenceEqual(["Beta/Cat-1", "Alpha/Cat-1"])
            && pClamped.Pagination.Page == 2
            && p2Factory.Items.SequenceEqual(p2.Items) && p2Factory.Pagination.TotalItems == 4
            && p2DbSet.Items.SequenceEqual(p2.Items) && p2DbSet.Pagination.TotalItems == 4
            && pHaving.Items.SequenceEqual(["Beta/Cat-1"]) && pHaving.Pagination is { TotalItems: 3, TotalPages: 2 }
            && pEmpty.Items.Count == 0 && pEmpty.Pagination is { Page: 1, TotalItems: 0, TotalPages: 1 }
            && pTenant1.Items.SequenceEqual(["Alpha/Cat-2"]) && pTenant1.Pagination is { TotalItems: 2, TotalPages: 2 }
            && pTenant2.Items.SequenceEqual(["Alpha/Cat-1"]) && pTenant2.Pagination is { TotalItems: 2, TotalPages: 2 }
            && pSmall.Pagination is { PageSize: 2, TotalItems: 4, TotalPages: 2 };

        var pUnordered1 = await repo.PagedListAsync(unorderedPagedSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var pUnordered2 = await factoryRepo.PagedListAsync(unorderedPagedSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var pUnorderedDb = await db.Products.ToPagedListAsync(unorderedPagedSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var unorderedPagingOk = pUnordered1.Pagination is { TotalItems: 4, TotalPages: 2 } && pUnordered1.Items.Count == 2
            && pUnordered2.Pagination is { TotalItems: 4, TotalPages: 2 } && pUnordered2.Items.Count == 2
            && pUnorderedDb.Pagination is { TotalItems: 4, TotalPages: 2 } && pUnorderedDb.Items.Count == 2;

        var pDistinct1 = await repo.PagedListAsync(postDistinctSpec, new PagingFilter { Page = 1, PageSize = 1 });
        var pDistinct2 = await factoryRepo.PagedListAsync(postDistinctSpec, new PagingFilter { Page = 2, PageSize = 1 });
        var pDistinctDb = await db.Products.ToPagedListAsync(postDistinctSpec, new PagingFilter { Page = 1, PageSize = 2 });
        var distinctPagingOk = pDistinct1.Pagination is { TotalItems: 2, TotalPages: 2 } && pDistinct1.Items.Count == 1
            && pDistinct2.Pagination is { TotalItems: 2, TotalPages: 2 } && pDistinct2.Items.Count == 1
            && pDistinctDb.Pagination is { TotalItems: 2, TotalPages: 1 } && pDistinctDb.Items.OrderBy(x => x).SequenceEqual([1, 2]);

        var clonedSliceSql = await repo.ListAsync(clonedSlice);
        var clonedSliceMem = clonedSlice.Evaluate(activeEntities).ToList();
        var templateSliceSql = await repo.ListAsync(cloneSliceTemplate);
        var clonedDistinctSql = (await repo.ListAsync(clonedDistinct)).OrderBy(x => x).ToList();
        var cloneOk = clonedSliceSql.SequenceEqual(["Alpha/Cat-2", "Beta/Cat-1", "Alpha/Cat-1"])
            && clonedSliceMem.SequenceEqual(clonedSliceSql)
            && templateSliceSql.SequenceEqual(["Alpha/Cat-2"])
            && clonedDistinct.IsDistinct
            && clonedDistinctSql.SequenceEqual([1, 2]);

        Console.WriteLine($"skip-take-distinct-paging: slice=[{string.Join(",", preSliceSql)}]/match={sliceMatch}; skip0={skipZeroOk}; take0={takeZeroOk}; dupGuards={dupSliceGuardsOk}; nullGuards={nullGuardsOk}; distinct={distinctOk}; orderDistinctBlocked={orderDistinctBlocked}; ensurePageable={ensurePageableOk}/skip0Pageable={skipZeroPageableOk}/blockedSql={blockedSqlCount}; orderedPaging={orderedPagingOk}; unorderedPaging={unorderedPagingOk}; distinctPaging={distinctPagingOk}; clone={cloneOk}");
    }

    private static async Task RunPostProcessingAndCache(DbContextOptions<GroupByContext> options)
    {
        var spec = new SingleResultSpecification<Product, string>();
        spec.Query
            .Where(p => p.Company!.Name == "Beta" && p.Category == "Cat-1")
            .GroupBy(p => p.Category)
            .Select(g => g.Key)
            .PostProcessingAction(items => items.Select(x => $"POST:{x}"))
            .EnableCache("GroupedCat", "Beta", 1);

        var condCacheFalse = new Specification<Product, string>();
        condCacheFalse.Query
            .GroupBy(p => p.Category)
            .Select(g => g.Key)
            .EnableCache("Ignored", false, "X")
            .WithCacheKey("AlsoIgnored", false);

        var withKeySpec = new Specification<Product, string>();
        withKeySpec.Query
            .GroupBy(p => p.Category)
            .Select(g => g.Key)
            .WithCacheKey("CustomKey");

        var emptyKeyThrows = ThrowsArgument(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).EnableCache("");
        }) && ThrowsArgument(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).WithCacheKey("");
        });

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var listItem = (await repo.ListAsync(spec)).Single();
        var dbSetListItem = (await db.Products.ToListAsync(spec)).Single();
        var dbSetEnumItem = (await db.Products.ToEnumerableAsync(spec)).Single();
        var memItem = spec.Evaluate(activeEntities).Single();
        var firstItem = await repo.FirstOrDefaultAsync(spec);
        var singleItem = await repo.SingleOrDefaultAsync(spec);
        var streamItems = new List<string>();
        await foreach (var item in repo.AsAsyncEnumerable(spec)) streamItems.Add(item);

        Console.WriteLine($"postprocessing-and-cache: list={listItem}; dbSetList={dbSetListItem}; dbSetEnum={dbSetEnumItem}; mem={memItem}; first={firstItem}; single={singleItem}; stream={streamItems.Single()}; cache={spec.CacheKey}/{spec.CacheEnabled}; withKey={withKeySpec.CacheKey}; condFalse={condCacheFalse.CacheEnabled}; emptyThrows={emptyKeyThrows}");
    }

    private static async Task RunValidationAndConflicts(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        var dupSpec = new Specification<Product, string>();
        dupSpec.Query.GroupBy(p => p.Category).Select(g => g.Key);
        var dupThrew = false;
        try
        {
            dupSpec.Query.GroupBy(p => p.Name);
        }
        catch (DuplicateGroupByException)
        {
            dupThrew = true;
        }

        var missingSelectSpec = new Specification<Product, string>();
        missingSelectSpec.Query.GroupBy(p => p.Category);

        var mixedSelectFirstSpec = new Specification<Product, string>();
        mixedSelectFirstSpec.Query.Select(p => p.Category);
        mixedSelectFirstSpec.Query.GroupBy(p => p.Category).Select(g => g.Key);

        var mixedSelectManySpec = new Specification<Product, string>();
        mixedSelectManySpec.Query.GroupBy(p => p.Category);
        mixedSelectManySpec.Query.SelectMany(p => new[] { p.Category });

        var preOrderSpec = new Specification<Product, string>();
        preOrderSpec.Query.OrderBy(p => p.Name).GroupBy(p => p.Category).Select(g => g.Key);

        var preSkipSpec = new Specification<Product, string>();
        preSkipSpec.Query.Skip(1).GroupBy(p => p.Category).Select(g => g.Key);

        var preTakeSpec = new Specification<Product, string>();
        preTakeSpec.Query.Take(1).GroupBy(p => p.Category).Select(g => g.Key);

        var includeIgnoredSpec = new Specification<Product, string>();
        includeIgnoredSpec.Query.Include(p => p.Company).Include("Company").GroupBy(p => p.Category).Select(g => g.Key);

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var dupUnmutated = dupSpec.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"]);

        sqlLog.Clear();
        var missingSelectOk = Throws<SelectorNotFoundException>(() => missingSelectSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<SelectorNotFoundException>(() => repo.ListAsync(missingSelectSpec))
            && await ThrowsAsync<SelectorNotFoundException>(() => db.Products.ToListAsync(missingSelectSpec));

        var concurrentOk = Throws<ConcurrentSelectorsException>(() => mixedSelectFirstSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<ConcurrentSelectorsException>(() => repo.ListAsync(mixedSelectFirstSpec))
            && Throws<ConcurrentSelectorsException>(() => mixedSelectManySpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<ConcurrentSelectorsException>(() => repo.ListAsync(mixedSelectManySpec));

        var preOrderOk = Throws<NotSupportedException>(() => preOrderSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(preOrderSpec));

        var prePagingOk = Throws<NotSupportedException>(() => preSkipSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(preSkipSpec))
            && Throws<NotSupportedException>(() => preTakeSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(preTakeSpec));

        var conflictSqlCount = sqlLog.Commands.Count;

        var includeIgnoredOk = (await repo.ListAsync(includeIgnoredSpec)).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"])
            && includeIgnoredSpec.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"]);

        Console.WriteLine($"validation-and-conflicts: dup={dupThrew}/{dupUnmutated}; missingSelect={missingSelectOk}; concurrent={concurrentOk}; preOrder={preOrderOk}; prePaging={prePagingOk}; conflictSql={conflictSqlCount}; includeIgnored={includeIgnoredOk}");
    }

    private static async Task RunCompositionAndCustomEvaluators(DbContextOptions<GroupByContext> options, SqlCaptureInterceptor sqlLog)
    {
        var sourceWithInclude = new Specification<Product>();
        sourceWithInclude.Query.Where(p => p.Company!.Name == "Alpha").Include(p => p.Company).Include("Company");

        var sourceWithOrder = new Specification<Product>();
        sourceWithOrder.Query.Where(p => p.Company!.Name == "Alpha").OrderBy(p => p.Name);

        var sourceWithSkip = new Specification<Product>();
        sourceWithSkip.Query.Where(p => p.Company!.Name == "Alpha").Skip(1);

        var groupedTemplate = new Specification<Product, string>();
        var groupedBuilder = groupedTemplate.Query.GroupBy(p => p.Category);
        groupedBuilder.Select(g => g.Key);

        var composed = sourceWithInclude.WithProjectionOf(groupedTemplate);
        // Mutating original template's selector after cloning must not leak into composed.
        groupedBuilder.Select(g => $"MUTATED:{g.Key}");

        var composedOrder = sourceWithOrder.WithProjectionOf(groupedTemplate);
        var composedSkip = sourceWithSkip.WithProjectionOf(groupedTemplate);

        await using var db = new GroupByContext(options);
        var repo = new ProductRepository(db);
        var customRepo = new CustomEvaluatorProductRepository(db);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var composedSql = (await repo.ListAsync(composed)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var composedMem = composed.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var templateMutated = groupedTemplate.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var composedConflictsOk = Throws<NotSupportedException>(() => composedOrder.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(composedOrder))
            && Throws<NotSupportedException>(() => composedSkip.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(composedSkip));

        var customEfList = (await customRepo.ListAsync(composed)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var customMemEval = new CustomInMemoryEvaluator();
        var customMemList = customMemEval.Evaluate(activeEntities, composed).OrderBy(x => x, StringComparer.Ordinal).ToList();

        sqlLog.Clear();
        var upcastCount = await repo.CountAsync((ISpecification<Product>)composed);
        var upcastAny = await repo.AnyAsync((ISpecification<Product>)composed);
        var upcastHasGroupBy = sqlLog.Commands.Any(c => c.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));

        var alphaProduct = new Product { Name = "X", Category = "Cat-1", Price = 10, Company = new Company { Name = "Alpha" } };
        var betaProduct = new Product { Name = "Y", Category = "Cat-1", Price = 10, Company = new Company { Name = "Beta" } };
        var satisfied = composed.IsSatisfiedBy(alphaProduct) && !composed.IsSatisfiedBy(betaProduct);

        Console.WriteLine($"composition-and-custom-evaluator: composed=[{string.Join(",", composedSql)}]; noLeak={composedSql.SequenceEqual(["Cat-1", "Cat-2"]) && composedMem.SequenceEqual(["Cat-1", "Cat-2"]) && templateMutated.SequenceEqual(["MUTATED:Cat-1", "MUTATED:Cat-2"])}; composedConflicts={composedConflictsOk}; customEf=[{string.Join(",", customEfList)}]; customMem=[{string.Join(",", customMemList)}]; upcast={upcastCount}/{upcastAny}/hasGroupBy={upcastHasGroupBy}; isSatisfiedBy={satisfied}");
    }

    private static string FormatCat(CategorySummary s)
        => string.Create(CultureInfo.InvariantCulture, $"{s.Category}:{s.Count}/{s.Sum}/{s.Min}/{s.Max}/{s.Avg:0}");

    private static string FormatComp(CompositeSummary s)
        => $"{s.CompanyName}/{s.Category}:{s.Count}/{s.Sum}";

    private static bool Throws<TEx>(Action action) where TEx : Exception
    {
        try { action(); return false; }
        catch (TEx) { return true; }
    }

    private static bool ThrowsArgument(Action action) => Throws<ArgumentException>(action);

    private static async Task<bool> ThrowsAsync<TEx>(Func<Task> action) where TEx : Exception
    {
        try { await action(); return false; }
        catch (TEx) { return true; }
    }

    private static async Task<bool> ThrowsArgSpecAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (ArgumentException ex) { return ex.ParamName == "specification"; }
    }
}

public sealed class CategorySummary
{
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public int Sum { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public double Avg { get; set; }
}

public sealed record CompositeKey
{
    public string CompanyName { get; init; } = "";
    public string Category { get; init; } = "";
}

public sealed class CompositeSummary
{
    public string CompanyName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public int Sum { get; set; }
}

public sealed class SqlCaptureInterceptor : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public void Clear() => Commands.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

public sealed class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public int Price { get; set; }
    public bool IsDeleted { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
}

public sealed class GroupByContext(DbContextOptions<GroupByContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
    }
}

public sealed class SimpleFactory(DbContextOptions<GroupByContext> options) : IDbContextFactory<GroupByContext>
{
    public GroupByContext CreateDbContext() => new(options);
}

public sealed class ProductRepository(GroupByContext context) : RepositoryBase<Product>(context);
public sealed class ProductFactoryRepository(IDbContextFactory<GroupByContext> factory) : ContextFactoryRepositoryBaseOfT<Product, GroupByContext>(factory);
public sealed class SmallPageProductRepository(GroupByContext context) : RepositoryBase<Product>(context)
{
    protected override PaginationSettings PaginationSettings { get; } = new(2, 3);
}
public sealed class TenantAlphaProductRepository(GroupByContext context) : RepositoryBase<Product>(context)
{
    protected override IQueryable<Product> ApplySpecification(ISpecification<Product> specification, bool evaluateCriteriaOnly = false)
        => SpecificationEvaluator.GetQuery(DbContext.Set<Product>().Where(p => p.Company!.Name == "Alpha"), specification, evaluateCriteriaOnly);

    protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<Product, TResult> specification)
        => SpecificationEvaluator.GetQuery(DbContext.Set<Product>().Where(p => p.Company!.Name == "Alpha"), specification);
}

public sealed class CustomEfSpecificationEvaluator() : SpecificationEvaluator([WhereEvaluator.Instance, SearchEvaluator.Instance, AsNoTrackingEvaluator.Instance]);
public sealed class CustomEvaluatorProductRepository(GroupByContext context) : RepositoryBase<Product>(context, new CustomEfSpecificationEvaluator());
public sealed class CustomInMemoryEvaluator() : InMemorySpecificationEvaluator([WhereEvaluator.Instance, SearchMemoryEvaluator.Instance]);

// Drives #99's core GroupBy().Select() support through the EF6 provider and In-Memory evaluator:
// single-key and composite-key (anonymous and member-initialized record/DTO) grouped projections with
// Count/Sum/Min/Max/Average, pre-group Where/Search/AsNoTracking, empty and constant-key groups,
// PostProcessingAction (list-only), EnableCache/WithCacheKey, builder/evaluator conflict validation,
// interim Distinct/PagedListAsync guards, WithProjectionOf cloning, custom evaluator inheritance,
// IsSatisfiedBy/upcast criteria-only behavior, and 10.x contract checks.
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using OpenSpecification;
using OpenSpecification.EntityFramework6;
using OpenSpecification.Paging;

const string Catalog = "VerifySpecificationGroupByEF6";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();

Database.SetInitializer<GroupByContext>(null);
using (var db = new GroupByContext(connectionString))
{
    if (db.Database.Exists()) db.Database.Delete();
    db.Database.Create();

    var alpha = new Company { Name = "Alpha" };
    var beta = new Company { Name = "Beta" };
    db.Companies.AddRange([alpha, beta]);
    await db.SaveChangesAsync();

    db.Products.AddRange([
        new Product { Name = "P01", Category = "Cat-1", Price = 10, CompanyId = alpha.Id },
        new Product { Name = "P02", Category = "Cat-1", Price = 30, CompanyId = alpha.Id },
        new Product { Name = "P03", Category = "Cat-2", Price = 20, CompanyId = alpha.Id },
        new Product { Name = "P04", Category = "Cat-2", Price = 40, CompanyId = alpha.Id },
        new Product { Name = "P05", Category = "Cat-1", Price = 50, CompanyId = beta.Id },
        new Product { Name = "P06", Category = "Cat-2", Price = 60, CompanyId = beta.Id },
        new Product { Name = "P07", Category = "Cat-2", Price = 80, CompanyId = beta.Id },
    ]);
    await db.SaveChangesAsync();
}

try
{
    await GroupByDriver.Run(connectionString);
}
finally
{
    SqlConnection.ClearAllPools();
    using var db = new GroupByContext(connectionString);
    if (db.Database.Exists()) db.Database.Delete();
}

return 0;

static class GroupByDriver
{
    public static async Task Run(string connectionString)
    {
        RunContractSurface();
        await RunSingleAndCompositeGroups(connectionString);
        await RunHavingAndOrdering(connectionString);
        await RunPostProcessingAndCache(connectionString);
        await RunValidationAndConflicts(connectionString);
        await RunCompositionAndCustomEvaluators(connectionString);
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

    private static async Task RunSingleAndCompositeGroups(string connectionString)
    {
        var allCatSpec = new Specification<Product, CategorySummary>();
        allCatSpec.Query
            .Search(p => p.Category, "Cat-%")
            .AsNoTracking()
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

        var alphaFilteredSpec = new Specification<Product, CategorySummary>();
        alphaFilteredSpec.Query
            .Where(p => p.Company.Name == "Alpha")
            .AsNoTracking()
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

        var singleGroupSpec = new SingleResultSpecification<Product, CategorySummary>();
        singleGroupSpec.Query
            .Where(p => p.Company.Name == "Beta" && p.Category == "Cat-1")
            .GroupBy(p => p.Category)
            .Select(g => new CategorySummary { Category = g.Key, Count = g.Count(), Sum = g.Sum(p => p.Price) });

        var anonCompositeSpec = new Specification<Product, CompositeSummary>();
        anonCompositeSpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
            .Select(g => new CompositeSummary { CompanyName = g.Key.CompanyName, Category = g.Key.Category, Count = g.Count(), Sum = g.Sum(p => p.Price) });

        var dtoCompositeSpec = new Specification<Product, CompositeSummary>();
        dtoCompositeSpec.Query
            .GroupBy(p => new CompositeKey { CompanyName = p.Company.Name, Category = p.Category })
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

        using var db = new GroupByContext(connectionString);
        var repo = new ProductRepository(db);

        var repoList = (await repo.ListAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var dbSetList = (await db.Products.ToListAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var dbSetEnum = (await db.Products.ToEnumerableAsync(allCatSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var withSpecList = (await db.Products.WithSpecification(allCatSpec).ToListAsync()).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
        var first = await repo.FirstOrDefaultAsync(singleGroupSpec);
        var single = await repo.SingleOrDefaultAsync(singleGroupSpec);

        var alphaList = (await repo.ListAsync(alphaFilteredSpec)).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();
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
            && singleStr == string.Join(",", memAll.Select(FormatCat));

        var anonStr = string.Join(",", anonList.Select(FormatComp));
        var dtoStr = string.Join(",", dtoList.Select(FormatComp));
        var compositeMatch = anonStr == dtoStr
            && anonStr == string.Join(",", memAnon.Select(FormatComp))
            && anonStr == string.Join(",", memDto.Select(FormatComp));

        Console.WriteLine($"groupby-single-and-composite: single=[{singleStr}]; surfacesMatch={surfacesMatch}; filtered=[{string.Join(",", alphaList.Select(x => $"{x.Category}:{x.Count}/{x.Sum}"))}]; first={first?.Category}:{first?.Sum}; singleRes={single?.Category}:{single?.Sum}; composite=[{anonStr}]; compositeMatch={compositeMatch}; empty={emptyKeyCount}/{emptyConstCount}/{memEmptyKey}/{memEmptyConst}");
    }

    private static async Task RunHavingAndOrdering(string connectionString)
    {
        var havingAndSpec = new Specification<Product, CompositeSummary>();
        havingAndSpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
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
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
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
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.CompanyName)
            .ThenByDescending(g => g.Sum(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var avgOrderSpec = new Specification<Product, string>();
        avgOrderSpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
            .OrderBy(g => g.Average(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var maxOrderSpec = new Specification<Product, string>();
        maxOrderSpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
            .OrderByDescending(g => g.Max(p => p.Price))
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var discardedPrimarySpec = new Specification<Product, string>();
        discardedPrimarySpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
            .OrderBy(g => g.Key.Category, condition: false)
            .ThenByDescending(g => g.Sum(p => p.Price))
            .OrderByDescending(g => g.Sum(p => p.Price), condition: true)
            .ThenBy(g => g.Key.CompanyName)
            .Select(g => g.Key.CompanyName + "/" + g.Key.Category);

        var nestedDiscardAndSavedHandleSpec = new Specification<Product, string>();
        var activeOrdHandle = nestedDiscardAndSavedHandleSpec.Query
            .GroupBy(p => new { CompanyName = p.Company.Name, p.Category })
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

        using var db = new GroupByContext(connectionString);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        var havingList = await repo.ListAsync(havingAndSpec);
        var orderThenHavingList = await repo.ListAsync(orderThenHavingSpec);
        var havingMem = havingAndSpec.Evaluate(activeEntities).ToList();

        var aggList = await repo.ListAsync(unprojectedAggOrderSpec);
        var aggDbSetList = await db.Products.ToListAsync(unprojectedAggOrderSpec);
        var aggDbSetEnum = (await db.Products.ToEnumerableAsync(unprojectedAggOrderSpec)).ToList();
        var aggWithSpec = await db.Products.WithSpecification(unprojectedAggOrderSpec).ToListAsync();
        var aggFirst = await repo.FirstOrDefaultAsync(unprojectedAggOrderSpec);
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

        sqlCommands.Clear();
        var dupOrderOk = Throws<DuplicateOrderChainException>(() => dupOrderDirectSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<DuplicateOrderChainException>(() => repo.ListAsync(dupOrderDirectSpec))
            && await ThrowsAsync<DuplicateOrderChainException>(() => db.Products.ToListAsync(dupOrderDirectSpec))
            && Throws<DuplicateOrderChainException>(() => dupOrderAcrossHavingSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<DuplicateOrderChainException>(() => repo.ListAsync(dupOrderAcrossHavingSpec));
        var dupOrderSql = sqlCommands.Count;

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

    private static async Task RunPostProcessingAndCache(string connectionString)
    {
        var spec = new SingleResultSpecification<Product, string>();
        spec.Query
            .Where(p => p.Company.Name == "Beta" && p.Category == "Cat-1")
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

        var emptyKeyThrows = Throws<ArgumentException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).EnableCache("");
        }) && Throws<ArgumentException>(() =>
        {
            var s = new Specification<Product, string>();
            s.Query.GroupBy(p => p.Category).Select(g => g.Key).WithCacheKey("");
        });

        using var db = new GroupByContext(connectionString);
        var repo = new ProductRepository(db);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var listItem = (await repo.ListAsync(spec)).Single();
        var dbSetListItem = (await db.Products.ToListAsync(spec)).Single();
        var dbSetEnumItem = (await db.Products.ToEnumerableAsync(spec)).Single();
        var memItem = spec.Evaluate(activeEntities).Single();
        var firstItem = await repo.FirstOrDefaultAsync(spec);
        var singleItem = await repo.SingleOrDefaultAsync(spec);

        Console.WriteLine($"postprocessing-and-cache: list={listItem}; dbSetList={dbSetListItem}; dbSetEnum={dbSetEnumItem}; mem={memItem}; first={firstItem}; single={singleItem}; cache={spec.CacheKey}/{spec.CacheEnabled}; withKey={withKeySpec.CacheKey}; condFalse={condCacheFalse.CacheEnabled}; emptyThrows={emptyKeyThrows}");
    }

    private static async Task RunValidationAndConflicts(string connectionString)
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

        var distinctGroupedSpec = new Specification<Product, string>();
        distinctGroupedSpec.Query.Distinct().GroupBy(p => p.Category).Select(g => g.Key);

        var includeIgnoredSpec = new Specification<Product, string>();
        includeIgnoredSpec.Query.Include(p => p.Company).Include("Company").GroupBy(p => p.Category).Select(g => g.Key);

        using var db = new GroupByContext(connectionString);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        var dupUnmutated = dupSpec.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"]);

        sqlCommands.Clear();
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

        var interimDistinctOk = Throws<NotSupportedException>(() => distinctGroupedSpec.Evaluate(activeEntities).ToList())
            && await ThrowsAsync<NotSupportedException>(() => repo.ListAsync(distinctGroupedSpec));

        var interimPagedOk = await ThrowsAsync<NotSupportedException>(() => repo.PagedListAsync(dupSpec, new PagingFilter { Page = 1, PageSize = 2 }))
            && await ThrowsAsync<NotSupportedException>(() => db.Products.ToPagedListAsync(dupSpec, new PagingFilter { Page = 1, PageSize = 2 }));

        var conflictSqlCount = sqlCommands.Count;

        var includeIgnoredOk = (await repo.ListAsync(includeIgnoredSpec)).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"])
            && includeIgnoredSpec.Evaluate(activeEntities).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["Cat-1", "Cat-2"]);

        Console.WriteLine($"validation-and-conflicts: dup={dupThrew}/{dupUnmutated}; missingSelect={missingSelectOk}; concurrent={concurrentOk}; preOrder={preOrderOk}; prePaging={prePagingOk}; interimDistinct={interimDistinctOk}; interimPaged={interimPagedOk}; conflictSql={conflictSqlCount}; includeIgnored={includeIgnoredOk}");
    }

    private static async Task RunCompositionAndCustomEvaluators(string connectionString)
    {
        var sourceWithInclude = new Specification<Product>();
        sourceWithInclude.Query.Where(p => p.Company.Name == "Alpha").Include(p => p.Company).Include("Company");

        var sourceWithOrder = new Specification<Product>();
        sourceWithOrder.Query.Where(p => p.Company.Name == "Alpha").OrderBy(p => p.Name);

        var sourceWithSkip = new Specification<Product>();
        sourceWithSkip.Query.Where(p => p.Company.Name == "Alpha").Skip(1);

        var groupedTemplate = new Specification<Product, string>();
        var groupedBuilder = groupedTemplate.Query.GroupBy(p => p.Category);
        groupedBuilder.Select(g => g.Key);

        var composed = sourceWithInclude.WithProjectionOf(groupedTemplate);
        groupedBuilder.Select(g => $"MUTATED:{g.Key}");

        var composedOrder = sourceWithOrder.WithProjectionOf(groupedTemplate);
        var composedSkip = sourceWithSkip.WithProjectionOf(groupedTemplate);

        using var db = new GroupByContext(connectionString);
        var activeEntities = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();

        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);
        var customRepo = new CustomEvaluatorProductRepository(db);

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

        sqlCommands.Clear();
        var upcastCount = await repo.CountAsync((ISpecification<Product>)composed);
        var upcastAny = await repo.AnyAsync((ISpecification<Product>)composed);
        var upcastHasGroupBy = sqlCommands.Any(c => c.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));

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

    private static async Task<bool> ThrowsAsync<TEx>(Func<Task> action) where TEx : Exception
    {
        try { await action(); return false; }
        catch (TEx) { return true; }
    }
}

public class CategorySummary
{
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public int Sum { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public double Avg { get; set; }
}

public record CompositeKey
{
    public string CompanyName { get; init; } = "";
    public string Category { get; init; } = "";
}

public class CompositeSummary
{
    public string CompanyName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public int Sum { get; set; }
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Price { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}

public class GroupByContext : DbContext
{
    public GroupByContext(string connectionString) : base(connectionString)
    {
        Configuration.LazyLoadingEnabled = false;
        Configuration.ProxyCreationEnabled = false;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Product> Products => Set<Product>();
}

public class ProductRepository(DbContext dbContext) : RepositoryBase<Product>(dbContext);
public class CustomEfSpecificationEvaluator() : SpecificationEvaluator([WhereEvaluator.Instance, SearchEvaluator.Instance, AsNoTrackingEvaluator.Instance]);
public class CustomEvaluatorProductRepository(DbContext dbContext) : RepositoryBase<Product>(dbContext, new CustomEfSpecificationEvaluator());
public class CustomInMemoryEvaluator() : InMemorySpecificationEvaluator([WhereEvaluator.Instance, SearchMemoryEvaluator.Instance]);

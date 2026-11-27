// Drives #98's Distinct() support through the EF6 provider and In-Memory evaluator: entity Distinct() before
// Include/OrderBy/Skip/Take, Select/SelectMany projection Distinct() with Skip/Take and PostProcessingAction,
// OrderBy + projection Distinct() NotSupportedException, upcast criteria-only CountAsync/AnyAsync without inner
// SELECT DISTINCT, Clone/WithProjectionOf propagation, EF6 up-front NotSupportedException for paged distinct
// projections on page 1 and page 2, and 10.x contract checks.
using System.Data.Entity;
using System.Data.SqlClient;
using OpenSpecification;
using OpenSpecification.EntityFramework6;
using OpenSpecification.Paging;

const string Catalog = "VerifySpecificationDistinctEF6";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();

Database.SetInitializer<DistinctContext>(null);
using (var db = new DistinctContext(connectionString))
{
    if (db.Database.Exists()) db.Database.Delete();
    db.Database.Create();

    var alpha = new Company { Name = "Alpha" };
    var beta = new Company { Name = "Beta" };
    db.Companies.AddRange([alpha, beta]);
    await db.SaveChangesAsync();

    var products = new List<Product>
    {
        new() { Name = "P01", Category = "Cat-1", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-A" }, new() { Label = "Tag-B" }] },
        new() { Name = "P02", Category = "Cat-1", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-A" }, new() { Label = "Tag-C" }] },
        new() { Name = "P03", Category = "Cat-2", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-B" }, new() { Label = "Tag-D" }] },
        new() { Name = "P04", Category = "Cat-2", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-C" }] },
        new() { Name = "P05", Category = "Cat-3", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-D" }, new() { Label = "Tag-E" }] },
        new() { Name = "P06", Category = "Cat-4", CompanyId = alpha.Id, Tags = [new() { Label = "Tag-E" }] },
        new() { Name = "P07", Category = "Cat-1", CompanyId = beta.Id,  Tags = [new() { Label = "Tag-A" }] },
        new() { Name = "P08", Category = "Cat-5", CompanyId = beta.Id,  Tags = [new() { Label = "Tag-E" }] },
        new() { Name = "P09", Category = "Cat-6", CompanyId = beta.Id,  Tags = [new() { Label = "Tag-B" }] },
    };
    db.Products.AddRange(products);
    await db.SaveChangesAsync();
}

try
{
    await DistinctDriver.Run(connectionString);
}
finally
{
    SqlConnection.ClearAllPools();
    using var db = new DistinctContext(connectionString);
    if (db.Database.Exists()) db.Database.Delete();
}

return 0;

static class DistinctDriver
{
    public static async Task Run(string connectionString)
    {
        RunContractAndInMemory();
        await RunEntityDistinct(connectionString);
        await RunProjectionSelectDistinct(connectionString);
        await RunProjectionSelectManyDistinct(connectionString);
        await RunOrderByConflict(connectionString);
        await RunUpcastCriteriaOnlyAndPropagation(connectionString);
        await RunPagedDistinctRefused(connectionString);
    }

    private static void RunContractAndInMemory()
    {
        var hasDistinctInterface = typeof(IDistinctSpecification).IsPublic && typeof(IDistinctSpecification).GetProperty("IsDistinct")?.PropertyType == typeof(bool);
        var specProp = typeof(Specification<Product>).GetProperty("IsDistinct");
        var evaluatorType = typeof(DistinctEvaluator);
        var evaluatorCriteria = DistinctEvaluator.Instance.IsCriteriaEvaluator;
        var ifaceUntouched = typeof(ISpecification<Product>).GetProperty("IsDistinct") is null
            && typeof(ISpecification<Product, string>).GetProperty("IsDistinct") is null
            && typeof(IReadRepositoryBase<Product>).GetMethods().All(m => m.Name != "Distinct");

        var valueSpec = new Specification<int>();
        valueSpec.Query.Where(x => x > 0).Distinct().OrderBy(x => x);
        var valueResult = valueSpec.Evaluate([3, 1, 2, 1, 3, -1]).ToList();

        var condFalseSpec = new Specification<int>();
        condFalseSpec.Query.Distinct(false);
        var condFalseResult = condFalseSpec.Evaluate([1, 1, 2]).ToList();

        var stickySpec = new Specification<int>();
        stickySpec.Query.Distinct(true).Distinct(false);
        var stickyResult = stickySpec.Evaluate([1, 1, 2]).ToList();

        Console.WriteLine($"contract-surface: iface={hasDistinctInterface}; prop={specProp?.PropertyType == typeof(bool)}; evalPublic={evaluatorType.IsPublic}; evalCriteria={evaluatorCriteria}; untouched={ifaceUntouched}");
        Console.WriteLine($"inmemory-value-types: distinct=[{string.Join(",", valueResult)}]; condFalse=[{string.Join(",", condFalseResult)}]; sticky=[{string.Join(",", stickyResult)}]");
    }

    private static async Task RunEntityDistinct(string connectionString)
    {
        var spec = new Specification<Product>();
        spec.Query
            .Where(p => p.Name.StartsWith("P"))
            .Search(p => p.Category, "Cat-%")
            .Distinct()
            .Include(p => p.Company)
            .OrderBy(p => p.Name)
            .Skip(1)
            .Take(2)
            .AsNoTracking();

        var singleSpec = new SingleResultSpecification<Product>();
        singleSpec.Query.Where(p => p.Name == "P01").Distinct().AsNoTracking();

        var pageSpec = new Specification<Product>();
        pageSpec.Query.Where(p => p.Category == "Cat-1").Distinct().OrderBy(p => p.Name).AsNoTracking();

        using var db = new DistinctContext(connectionString);
        var repo = new ProductRepository(db);

        var list = await repo.ListAsync(spec);
        var count = await repo.CountAsync(spec);
        var any = await repo.AnyAsync(spec);
        var first = await repo.FirstOrDefaultAsync(spec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var withSpecCount = await db.Products.WithSpecification(spec).CountAsync();
        var repoPage = await repo.PagedListAsync(pageSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var dbSetPage = await db.Products.ToPagedListAsync(pageSpec, new PagingFilter { Page = 1, PageSize = 2 });

        var listSummary = string.Join(",", list.Select(p => $"{p.Name}({p.Company?.Name})"));
        Console.WriteLine($"entity-distinct: list=[{listSummary}]; count={count}; any={any}; first={first?.Name}; single={single?.Name}; withSpecCount={withSpecCount}; repoPage=[{string.Join(",", repoPage.Items.Select(p => p.Name))}]/{repoPage.Pagination.TotalItems}; dbSetPage=[{string.Join(",", dbSetPage.Items.Select(p => p.Name))}]/{dbSetPage.Pagination.TotalItems}");
    }

    private static async Task RunProjectionSelectDistinct(string connectionString)
    {
        // Note: EF6 requires OrderBy before Skip, so Skip(0) + Take(2) tests post-Distinct Take and Skip(0) omission.
        var takeSpec = new Specification<Product, string>();
        takeSpec.Query
            .Where(p => p.Company.Name == "Alpha")
            .Distinct()
            .Skip(0)
            .Take(2)
            .PostProcessingAction(items => items.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"*{x}"))
            .Select(p => p.Category);

        var allSpec = new Specification<Product, string>();
        allSpec.Query
            .Where(p => p.Company.Name == "Alpha")
            .Distinct()
            .Select(p => p.Category);

        var singleSpec = new SingleResultSpecification<Product, string>();
        singleSpec.Query.Where(p => p.Category == "Cat-1").Distinct().Select(p => p.Category);

        using var db = new DistinctContext(connectionString);
        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        var allRows = (await repo.ListAsync(allSpec)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        sqlCommands.Clear();
        var repoList = await repo.ListAsync(takeSpec);
        var skipZeroHasRowNumber = sqlCommands.Any(c => c.Contains("row_number", StringComparison.OrdinalIgnoreCase) || c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        var dbSetList = await db.Products.ToListAsync(takeSpec);
        var dbSetEnum = (await db.Products.ToEnumerableAsync(takeSpec)).ToList();
        var first = await repo.FirstOrDefaultAsync(takeSpec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var withSpecCount = await db.Products.WithSpecification(allSpec).CountAsync();

        var allProducts = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();
        var memList = takeSpec.Evaluate(allProducts).ToList();

        Console.WriteLine($"projection-select-distinct: all=[{string.Join(",", allRows)}]; repoCount={repoList.Count}; postPrefix={repoList.All(x => x.StartsWith('*'))}; dbSetMatch={dbSetList.Count == 2 && dbSetEnum.Count == 2}; firstNoPost={first is not null && !first.StartsWith('*')}; single={single}; skipZeroHasRowNumber={skipZeroHasRowNumber}; withSpecCount={withSpecCount}; memCount={memList.Count}");
    }

    private static async Task RunProjectionSelectManyDistinct(string connectionString)
    {
        var allDistinctSpec = new Specification<Product, string>();
        allDistinctSpec.Query
            .Distinct()
            .PostProcessingAction(items => items.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"#{x}"))
            .SelectMany(p => p.Tags.Select(t => t.Label));

        var takeSpec = new Specification<Product, string>();
        takeSpec.Query.Distinct().Take(2).SelectMany(p => p.Tags.Select(t => t.Label));

        using var db = new DistinctContext(connectionString);
        var repo = new ProductRepository(db);

        var allTags = await repo.ListAsync(allDistinctSpec);
        var taken = await repo.ListAsync(takeSpec);
        var withSpecCount = await db.Products.WithSpecification(allDistinctSpec).CountAsync();

        var allProducts = await db.Products.Include(p => p.Tags).AsNoTracking().ToListAsync();
        var memAll = allDistinctSpec.Evaluate(allProducts).ToList();
        var memTaken = takeSpec.Evaluate(allProducts).ToList();

        Console.WriteLine($"projection-selectmany-distinct: all=[{string.Join(",", allTags)}]; takenCount={taken.Count}; withSpecCount={withSpecCount}; memAll=[{string.Join(",", memAll)}]; memTakenCount={memTaken.Count}");
    }

    private static async Task RunOrderByConflict(string connectionString)
    {
        var selectSpec = new Specification<Product, string>();
        selectSpec.Query.OrderBy(p => p.Name).Distinct().Select(p => p.Category);

        var selectManySpec = new Specification<Product, string>();
        selectManySpec.Query.OrderByDescending(p => p.Name).Distinct().SelectMany(p => p.Tags.Select(t => t.Label));

        using var db = new DistinctContext(connectionString);
        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        var memThrew = ThrowsNotSupported(() => selectSpec.Evaluate([]).ToList())
            && ThrowsNotSupported(() => selectManySpec.Evaluate([]).ToList());

        sqlCommands.Clear();
        var repoSelectThrew = await ThrowsNotSupportedAsync(() => repo.ListAsync(selectSpec));
        var repoSelectManyThrew = await ThrowsNotSupportedAsync(() => repo.ListAsync(selectManySpec));
        var dbSetThrew = await ThrowsNotSupportedAsync(() => db.Products.ToListAsync(selectSpec));
        var sqlCount = sqlCommands.Count;

        Console.WriteLine($"conflict-orderby-distinct: mem={memThrew}; repoSelect={repoSelectThrew}; repoSelectMany={repoSelectManyThrew}; dbSet={dbSetThrew}; sqlCommands={sqlCount}");
    }

    private static async Task RunUpcastCriteriaOnlyAndPropagation(string connectionString)
    {
        var projSpec = new Specification<Product, string>();
        projSpec.Query.Where(p => p.Company.Name == "Alpha").Distinct().Select(p => p.Category);

        using var db = new DistinctContext(connectionString);
        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        sqlCommands.Clear();
        var upcastCount = await repo.CountAsync((ISpecification<Product>)projSpec);
        var upcastAny = await repo.AnyAsync((ISpecification<Product>)projSpec);
        var hasDistinctSql = sqlCommands.Any(c => c.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));

        var alphaProduct = new Product { Name = "X", Category = "Cat-1", Company = new Company { Name = "Alpha" } };
        var betaProduct = new Product { Name = "Y", Category = "Cat-1", Company = new Company { Name = "Beta" } };
        var satisfied = projSpec.IsSatisfiedBy(alphaProduct) && !projSpec.IsSatisfiedBy(betaProduct);

        var baseDistinct = new Specification<Product>();
        baseDistinct.Query.Where(p => p.Name.StartsWith("P")).Distinct();
        var baseNonDistinct = new Specification<Product>();
        baseNonDistinct.Query.Where(p => p.Name.StartsWith("P"));

        var projDistinct = new Specification<Product, string>();
        projDistinct.Query.Distinct().Select(p => p.Category);
        var projNonDistinct = new Specification<Product, string>();
        projNonDistinct.Query.Select(p => p.Category);

        var falseToTrue = baseNonDistinct.WithProjectionOf(projDistinct).IsDistinct;
        var trueToFalse = baseDistinct.WithProjectionOf(projNonDistinct).IsDistinct;

        Console.WriteLine($"upcast-criteria-only: count={upcastCount}; any={upcastAny}; sqlHasDistinct={hasDistinctSql}; isSatisfiedBy={satisfied}; withProjFalseToTrue={falseToTrue}; withProjTrueToFalse={trueToFalse}");
    }

    private static async Task RunPagedDistinctRefused(string connectionString)
    {
        var selectSpec = new Specification<Product, string>();
        selectSpec.Query.Distinct().Select(p => p.Category);

        var selectManySpec = new Specification<Product, string>();
        selectManySpec.Query.Distinct().SelectMany(p => p.Tags.Select(t => t.Label));

        using var db = new DistinctContext(connectionString);
        var sqlCommands = new List<string>();
        db.Database.Log = msg => { if (msg.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) sqlCommands.Add(msg); };
        var repo = new ProductRepository(db);

        sqlCommands.Clear();
        var repoSelectP1 = await ThrowsNotSupportedAsync(() => repo.PagedListAsync(selectSpec, new PagingFilter { Page = 1, PageSize = 2 }));
        var repoSelectP2 = await ThrowsNotSupportedAsync(() => repo.PagedListAsync(selectSpec, new PagingFilter { Page = 2, PageSize = 2 }));
        var dbSetSelectP1 = await ThrowsNotSupportedAsync(() => db.Products.ToPagedListAsync(selectSpec, new PagingFilter { Page = 1, PageSize = 2 }));
        var repoSelectManyP1 = await ThrowsNotSupportedAsync(() => repo.PagedListAsync(selectManySpec, new PagingFilter { Page = 1, PageSize = 2 }));
        var dbSetSelectManyP2 = await ThrowsNotSupportedAsync(() => db.Products.ToPagedListAsync(selectManySpec, new PagingFilter { Page = 2, PageSize = 2 }));
        var sqlCount = sqlCommands.Count;

        Console.WriteLine($"ef6-paged-distinct-refused: repoSelectP1={repoSelectP1}; repoSelectP2={repoSelectP2}; dbSetSelectP1={dbSetSelectP1}; repoSelectManyP1={repoSelectManyP1}; dbSetSelectManyP2={dbSetSelectManyP2}; sqlCommands={sqlCount}");
    }

    private static bool ThrowsNotSupported(Action action)
    {
        try { action(); return false; }
        catch (NotSupportedException) { return true; }
    }

    private static async Task<bool> ThrowsNotSupportedAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (NotSupportedException) { return true; }
    }
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
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public List<ProductTag> Tags { get; set; } = [];
}

public class ProductTag
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

public class DistinctContext : DbContext
{
    public DistinctContext(string connectionString) : base(connectionString)
    {
        Configuration.LazyLoadingEnabled = false;
        Configuration.ProxyCreationEnabled = false;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
}

public class ProductRepository(DbContext dbContext) : RepositoryBase<Product>(dbContext);

// Drives #98's Distinct() support through the EF Core provider and In-Memory evaluator: entity Distinct() before
// Include/OrderBy/Skip/Take, Select/SelectMany projection Distinct() with Skip/Take and PostProcessingAction,
// OrderBy + projection Distinct() NotSupportedException, upcast criteria-only CountAsync/AnyAsync without inner
// SELECT DISTINCT, Clone/WithProjectionOf propagation, EF Core paged distinct projections (including custom
// ApplySpecification override), and 10.x contract checks.
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using OpenSpecification.Paging;

const string Catalog = "VerifySpecificationDistinctEFCore";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var sqlLog = new SqlCaptureInterceptor();
var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();
var options = new DbContextOptionsBuilder<DistinctContext>()
    .UseSqlServer(connectionString)
    .AddInterceptors(sqlLog)
    .Options;

await using (var db = new DistinctContext(options))
{
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();

    var alpha = new Company { Name = "Alpha" };
    var beta = new Company { Name = "Beta" };
    db.Companies.AddRange(alpha, beta);
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
    await DistinctDriver.Run(options, sqlLog);
}
finally
{
    SqlConnection.ClearAllPools();
    await using var db = new DistinctContext(options);
    await db.Database.EnsureDeletedAsync();
}

return 0;

static class DistinctDriver
{
    public static async Task Run(DbContextOptions<DistinctContext> options, SqlCaptureInterceptor sqlLog)
    {
        RunContractAndInMemory();
        await RunEntityDistinct(options);
        await RunProjectionSelectDistinct(options, sqlLog);
        await RunProjectionSelectManyDistinct(options);
        await RunOrderByConflict(options, sqlLog);
        await RunUpcastCriteriaOnlyAndPropagation(options, sqlLog);
        await RunPagedDistinctSelect(options, sqlLog);
        await RunPagedDistinctSelectMany(options);
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

    private static async Task RunEntityDistinct(DbContextOptions<DistinctContext> options)
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

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);

        var list = await repo.ListAsync(spec);
        var count = await repo.CountAsync(spec);
        var any = await repo.AnyAsync(spec);
        var first = await repo.FirstOrDefaultAsync(spec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);

        var streamed = new List<string>();
        await foreach (var item in repo.AsAsyncEnumerable(spec))
        {
            streamed.Add(item.Name);
        }

        var withSpecCount = await db.Products.WithSpecification(spec).CountAsync();
        var repoPage = await repo.PagedListAsync(pageSpec, new PagingFilter { Page = 2, PageSize = 2 });
        var dbSetPage = await db.Products.ToPagedListAsync(pageSpec, new PagingFilter { Page = 1, PageSize = 2 });

        var listSummary = string.Join(",", list.Select(p => $"{p.Name}({p.Company?.Name})"));
        Console.WriteLine($"entity-distinct: list=[{listSummary}]; count={count}; any={any}; first={first?.Name}; single={single?.Name}; stream=[{string.Join(",", streamed)}]; withSpecCount={withSpecCount}; repoPage=[{string.Join(",", repoPage.Items.Select(p => p.Name))}]/{repoPage.Pagination.TotalItems}; dbSetPage=[{string.Join(",", dbSetPage.Items.Select(p => p.Name))}]/{dbSetPage.Pagination.TotalItems}");
    }

    private static async Task RunProjectionSelectDistinct(DbContextOptions<DistinctContext> options, SqlCaptureInterceptor sqlLog)
    {
        var spec = new Specification<Product, string>();
        spec.Query
            .Where(p => p.Company!.Name == "Alpha")
            .Distinct()
            .Skip(1)
            .Take(2)
            .PostProcessingAction(items => items.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"*{x}"))
            .Select(p => p.Category);

        var skipZeroSpec = new Specification<Product, string>();
        skipZeroSpec.Query
            .Where(p => p.Company!.Name == "Alpha")
            .Distinct()
            .Skip(0)
            .Take(4)
            .Select(p => p.Category);

        var singleSpec = new SingleResultSpecification<Product, string>();
        singleSpec.Query.Where(p => p.Category == "Cat-1").Distinct().Select(p => p.Category);

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);

        var repoList = await repo.ListAsync(spec);
        var dbSetList = await db.Products.ToListAsync(spec);
        var dbSetEnum = (await db.Products.ToEnumerableAsync(spec)).ToList();
        var first = await repo.FirstOrDefaultAsync(spec);
        var single = await repo.SingleOrDefaultAsync(singleSpec);
        var streamed = new List<string>();
        await foreach (var cat in repo.AsAsyncEnumerable(spec))
        {
            streamed.Add(cat);
        }

        sqlLog.Clear();
        var skipZeroRows = (await repo.ListAsync(skipZeroSpec)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var skipZeroHasOffset = sqlLog.Commands.Any(c => c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        var allProducts = await db.Products.Include(p => p.Company).AsNoTracking().ToListAsync();
        var memList = spec.Evaluate(allProducts).ToList();

        var withSpecCount = await db.Products.WithSpecification(skipZeroSpec).CountAsync();

        Console.WriteLine($"projection-select-distinct: repoCount={repoList.Count}; postPrefix={repoList.All(x => x.StartsWith('*'))}; dbSetMatch={dbSetList.Count == 2 && dbSetList.All(x => x.StartsWith('*')) && dbSetEnum.Count == 2 && dbSetEnum.All(x => x.StartsWith('*'))}; firstNoPost={first is not null && !first.StartsWith('*')}; single={single}; streamNoPost={streamed.Count == 2 && streamed.All(x => !x.StartsWith('*'))}; skipZero=[{string.Join(",", skipZeroRows)}]; skipZeroHasOffset={skipZeroHasOffset}; withSpecCount={withSpecCount}; memCount={memList.Count}");
    }

    private static async Task RunProjectionSelectManyDistinct(DbContextOptions<DistinctContext> options)
    {
        var spec = new Specification<Product, string>();
        spec.Query
            .Distinct()
            .Skip(1)
            .Take(2)
            .PostProcessingAction(items => items.Select(x => $"#{x}"))
            .SelectMany(p => p.Tags.Select(t => t.Label));

        var allDistinctSpec = new Specification<Product, string>();
        allDistinctSpec.Query.Distinct().SelectMany(p => p.Tags.Select(t => t.Label));

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);

        var allTags = (await repo.ListAsync(allDistinctSpec)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sliced = await repo.ListAsync(spec);
        var withSpecCount = await db.Products.WithSpecification(allDistinctSpec).CountAsync();

        var allProducts = await db.Products.Include(p => p.Tags).AsNoTracking().ToListAsync();
        var memAll = allDistinctSpec.Evaluate(allProducts).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var memSliced = spec.Evaluate(allProducts).ToList();

        Console.WriteLine($"projection-selectmany-distinct: all=[{string.Join(",", allTags)}]; slicedCount={sliced.Count}; slicedPost={sliced.All(x => x.StartsWith('#'))}; withSpecCount={withSpecCount}; memAll=[{string.Join(",", memAll)}]; memSlicedCount={memSliced.Count}");
    }

    private static async Task RunOrderByConflict(DbContextOptions<DistinctContext> options, SqlCaptureInterceptor sqlLog)
    {
        var selectSpec = new Specification<Product, string>();
        selectSpec.Query.OrderBy(p => p.Name).Distinct().Select(p => p.Category);

        var selectManySpec = new Specification<Product, string>();
        selectManySpec.Query.OrderByDescending(p => p.Name).Distinct().SelectMany(p => p.Tags.Select(t => t.Label));

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);

        var memThrew = ThrowsNotSupported(() => selectSpec.Evaluate([]).ToList())
            && ThrowsNotSupported(() => selectManySpec.Evaluate([]).ToList());

        sqlLog.Clear();
        var repoSelectThrew = await ThrowsNotSupportedAsync(() => repo.ListAsync(selectSpec));
        var repoSelectManyThrew = await ThrowsNotSupportedAsync(() => repo.ListAsync(selectManySpec));
        var dbSetThrew = await ThrowsNotSupportedAsync(() => db.Products.ToListAsync(selectSpec));
        var sqlCount = sqlLog.Commands.Count;

        Console.WriteLine($"conflict-orderby-distinct: mem={memThrew}; repoSelect={repoSelectThrew}; repoSelectMany={repoSelectManyThrew}; dbSet={dbSetThrew}; sqlCommands={sqlCount}");
    }

    private static async Task RunUpcastCriteriaOnlyAndPropagation(DbContextOptions<DistinctContext> options, SqlCaptureInterceptor sqlLog)
    {
        var projSpec = new Specification<Product, string>();
        projSpec.Query.Where(p => p.Company!.Name == "Alpha").Distinct().Select(p => p.Category);

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);

        sqlLog.Clear();
        var upcastCount = await repo.CountAsync((ISpecification<Product>)projSpec);
        var upcastAny = await repo.AnyAsync((ISpecification<Product>)projSpec);
        var hasDistinctSql = sqlLog.Commands.Any(c => c.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));

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

    private static async Task RunPagedDistinctSelect(DbContextOptions<DistinctContext> options, SqlCaptureInterceptor sqlLog)
    {
        var spec = new Specification<Product, string>();
        spec.Query
            .Distinct()
            .PostProcessingAction(items => items.Select(x => $"IGNORED-{x}"))
            .Select(p => p.Category);

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);
        var factoryRepo = new ProductFactoryRepository(new SimpleFactory(options));
        var alphaOnlyRepo = new AlphaTenantProductRepository(db);

        var page1 = await repo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var page2 = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });
        var page99 = await repo.PagedListAsync(spec, new PagingFilter { Page = 99, PageSize = 2 });
        var factoryPage1 = await factoryRepo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var dbSetPage2 = await db.Products.ToPagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });
        var tenantPage1 = await alphaOnlyRepo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });

        var skipSpec = new Specification<Product, string>();
        skipSpec.Query.Distinct().Skip(1).Select(p => p.Category);
        sqlLog.Clear();
        var skipRejected = await ThrowsArgumentExceptionAsync(() => repo.PagedListAsync(skipSpec, new PagingFilter { Page = 1, PageSize = 2 }));
        var skipSqlCount = sqlLog.Commands.Count;

        var noPost = page1.Items.All(x => !x.StartsWith("IGNORED-"));
        Console.WriteLine($"paged-distinct-select: p1={page1.Items.Count}/{page1.Pagination.TotalItems}/tp{page1.Pagination.TotalPages}; p2={page2.Items.Count}/p{page2.Pagination.Page}; p99=p{page99.Pagination.Page}/{page99.Items.Count}; factoryTotal={factoryPage1.Pagination.TotalItems}; dbSetTotal={dbSetPage2.Pagination.TotalItems}; tenantTotal={tenantPage1.Pagination.TotalItems}/tp{tenantPage1.Pagination.TotalPages}; noPost={noPost}; skipRejected={skipRejected}; skipSql={skipSqlCount}");
    }

    private static async Task RunPagedDistinctSelectMany(DbContextOptions<DistinctContext> options)
    {
        var spec = new Specification<Product, string>();
        spec.Query.Distinct().SelectMany(p => p.Tags.Select(t => t.Label));

        await using var db = new DistinctContext(options);
        var repo = new ProductRepository(db);
        var factoryRepo = new ProductFactoryRepository(new SimpleFactory(options));

        var p1 = await repo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var p2 = await repo.PagedListAsync(spec, new PagingFilter { Page = 2, PageSize = 2 });
        var p3 = await repo.PagedListAsync(spec, new PagingFilter { Page = 3, PageSize = 2 });
        var factoryP1 = await factoryRepo.PagedListAsync(spec, new PagingFilter { Page = 1, PageSize = 2 });
        var dbSetP3 = await db.Products.ToPagedListAsync(spec, new PagingFilter { Page = 3, PageSize = 2 });

        Console.WriteLine($"paged-distinct-selectmany: p1={p1.Items.Count}/{p1.Pagination.TotalItems}/tp{p1.Pagination.TotalPages}; p2={p2.Items.Count}; p3={p3.Items.Count}/hasNext={p3.Pagination.HasNext}; factoryTotal={factoryP1.Pagination.TotalItems}; dbSetP3={dbSetP3.Items.Count}/{dbSetP3.Pagination.TotalItems}");
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

    private static async Task<bool> ThrowsArgumentExceptionAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (ArgumentException) { return true; }
    }
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
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public List<ProductTag> Tags { get; set; } = [];
}

public sealed class ProductTag
{
    public int Id { get; set; }
    public required string Label { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
}

public sealed class DistinctContext(DbContextOptions<DistinctContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
}

public sealed class SimpleFactory(DbContextOptions<DistinctContext> options) : IDbContextFactory<DistinctContext>
{
    public DistinctContext CreateDbContext() => new(options);
}

public sealed class ProductRepository(DistinctContext context) : RepositoryBase<Product>(context);
public sealed class ProductFactoryRepository(IDbContextFactory<DistinctContext> factory) : ContextFactoryRepositoryBaseOfT<Product, DistinctContext>(factory);

public sealed class AlphaTenantProductRepository(DistinctContext context) : RepositoryBase<Product>(context)
{
    protected override IQueryable<Product> ApplySpecification(ISpecification<Product> specification, bool evaluateCriteriaOnly = false)
        => SpecificationEvaluator.GetQuery(DbContext.Set<Product>().Where(p => p.Company!.Name == "Alpha"), specification, evaluateCriteriaOnly);

    protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<Product, TResult> specification)
        => SpecificationEvaluator.GetQuery(DbContext.Set<Product>().Where(p => p.Company!.Name == "Alpha"), specification);
}

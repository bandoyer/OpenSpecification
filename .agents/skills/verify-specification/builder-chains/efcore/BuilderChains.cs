// Drives #78's per-chain builder isolation through the EF Core provider: interleaved Include/OrderBy/EnableCache
// chains (active and discarded), cross-thread continuations, and 10.x IsChainDiscarded binary compatibility.
// Seeds its own SQL Server database, prints deterministic "label: result" lines, and drops it.
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;

const string Catalog = "VerifySpecificationBuilderChainsEFCore";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var options = new DbContextOptionsBuilder<ChainContext>()
    .UseSqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString())
    .Options;

await using (var db = new ChainContext(options))
{
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();

    var alpha = new Company
    {
        Name = "Alpha",
        Stores =
        [
            new Store { Name = "Alpha-North", Products = [new Product { Category = "A", Name = "A1" }, new Product { Category = "A", Name = "A2" }] },
            new Store { Name = "Alpha-South", Products = [new Product { Category = "B", Name = "B1" }, new Product { Category = "B", Name = "B2" }] },
        ]
    };
    var beta = new Company
    {
        Name = "Beta",
        Stores =
        [
            new Store { Name = "Beta-North", Products = [new Product { Category = "A", Name = "A3" }, new Product { Category = "A", Name = "A4" }] },
            new Store { Name = "Beta-South", Products = [new Product { Category = "B", Name = "B3" }, new Product { Category = "B", Name = "B4" }] },
        ]
    };
    db.Companies.AddRange(alpha, beta);
    await db.SaveChangesAsync();
}

try
{
    await ChainDriver.Run(options);
}
finally
{
    SqlConnection.ClearAllPools();
    await using var db = new ChainContext(options);
    await db.Database.EnsureDeletedAsync();
}

return 0;

static class ChainDriver
{
    public static async Task Run(DbContextOptions<ChainContext> options)
    {
        await RunOrphanIncludeViaOrderBy(options);
        await RunOrphanOrderByViaCache(options);
        await RunDropIncludeViaOrderBy(options);
        await RunDropOrderByViaCache(options);
        await RunThreadHopDiscardedInclude(options);
        await RunThreadHopActiveInclude(options);
        RunCompatFieldCheck();
    }

    private static async Task RunOrphanIncludeViaOrderBy(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: false);
        spec.Query.OrderBy(c => c.Name, condition: true);
        inc.ThenInclude(s => s.Products);
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new CompanyRepository(db);
        var rows = await repo.ListAsync(spec);
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"orphan-include-via-orderby: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunOrphanOrderByViaCache(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Company>();
        var ord = spec.Query.OrderBy(c => c.Name, condition: false);
        spec.Query.EnableCache("companies", condition: true);
        ord.ThenBy(c => c.Id).ThenBy(c => c.Name);
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new CompanyRepository(db);
        var rows = await repo.ListAsync(spec);
        var inMemory = spec.Evaluate(rows).ToList();
        Console.WriteLine($"orphan-orderby-via-cache: orders={spec.OrderExpressions.Count()}; sqlCount={rows.Count}; memCount={inMemory.Count}");
    }

    private static async Task RunDropIncludeViaOrderBy(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: true);
        spec.Query.OrderBy(c => c.Name, condition: false);
        inc.ThenInclude(s => s.Products);
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new CompanyRepository(db);
        var rows = (await repo.ListAsync(spec)).OrderBy(c => c.Name).ToList();
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"drop-include-via-orderby: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunDropOrderByViaCache(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Product>();
        var ord = spec.Query.OrderBy(p => p.Category, condition: true);
        spec.Query.EnableCache("products", condition: false);
        ord.ThenByDescending(p => p.Name);
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new ProductRepository(db);
        var rows = await repo.ListAsync(spec);
        var names = string.Join(", ", rows.Select(p => p.Name));
        Console.WriteLine($"drop-orderby-via-cache: orders={spec.OrderExpressions.Count()}; names=[{names}]");
    }

    private static async Task RunThreadHopDiscardedInclude(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: false);
        await RunOnFreshThreadAsync(() => inc.ThenInclude(s => s.Products));
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new CompanyRepository(db);
        var rows = (await repo.ListAsync(spec)).OrderBy(c => c.Name).ToList();
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"thread-hop-discarded-include: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunThreadHopActiveInclude(DbContextOptions<ChainContext> options)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: true);
        await RunOnFreshThreadAsync(() =>
        {
            new Specification<Company>().Query.Include(c => c.Stores, condition: false);
            inc.ThenInclude(s => s.Products);
        });
        spec.Query.AsNoTracking();

        await using var db = new ChainContext(options);
        var repo = new CompanyRepository(db);
        var rows = (await repo.ListAsync(spec)).OrderBy(c => c.Name).ToList();
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"thread-hop-active-include: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static void RunCompatFieldCheck()
    {
        var field = typeof(Specification<Company>).GetField("IsChainDiscarded");
        var isObsolete = field?.IsDefined(typeof(ObsoleteAttribute), false) ?? false;
        Console.WriteLine($"compat-field: present={field is not null}; public={field?.IsPublic}; static={field?.IsStatic}; type={field?.FieldType.Name}; obsolete={isObsolete}");
    }

    private static Task RunOnFreshThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }).Start();
        return tcs.Task;
    }
}

public sealed class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Store> Stores { get; set; } = [];
}

public sealed class Store
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public List<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Category { get; set; }
    public required string Name { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }
}

public sealed class ChainContext(DbContextOptions<ChainContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Product> Products => Set<Product>();
}

public sealed class CompanyRepository(ChainContext context) : RepositoryBase<Company>(context);
public sealed class ProductRepository(ChainContext context) : RepositoryBase<Product>(context);

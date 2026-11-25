// Drives #78's per-chain builder isolation through the EF6 provider: interleaved Include/OrderBy/EnableCache
// chains (active and discarded), cross-thread continuations, and 10.x IsChainDiscarded binary compatibility.
// Seeds its own SQL Server database, prints deterministic "label: result" lines, and drops it.
using System.Data.Entity;
using System.Data.SqlClient;
using OpenSpecification;
using OpenSpecification.EntityFramework6;

const string Catalog = "VerifySpecificationBuilderChainsEF6";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();

Database.SetInitializer<ChainContext>(null);
using (var db = new ChainContext(connectionString))
{
    if (db.Database.Exists()) db.Database.Delete();
    db.Database.Create();

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
    db.Companies.AddRange([alpha, beta]);
    await db.SaveChangesAsync();
}

try
{
    await ChainDriver.Run(connectionString);
}
finally
{
    SqlConnection.ClearAllPools();
    using var db = new ChainContext(connectionString);
    if (db.Database.Exists()) db.Database.Delete();
}

return 0;

static class ChainDriver
{
    public static async Task Run(string connectionString)
    {
        await RunOrphanIncludeViaOrderBy(connectionString);
        await RunOrphanOrderByViaCache(connectionString);
        await RunDropIncludeViaOrderBy(connectionString);
        await RunDropOrderByViaCache(connectionString);
        await RunThreadHopDiscardedInclude(connectionString);
        await RunThreadHopActiveInclude(connectionString);
        RunCompatFieldCheck();
    }

    private static async Task RunOrphanIncludeViaOrderBy(string connectionString)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: false);
        spec.Query.OrderBy(c => c.Name, condition: true);
        inc.ThenInclude(s => s.Products);
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
        var repo = new CompanyRepository(db);
        var rows = await repo.ListAsync(spec);
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"orphan-include-via-orderby: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunOrphanOrderByViaCache(string connectionString)
    {
        var spec = new Specification<Company>();
        var ord = spec.Query.OrderBy(c => c.Name, condition: false);
        spec.Query.EnableCache("companies", condition: true);
        ord.ThenBy(c => c.Id).ThenBy(c => c.Name);
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
        var repo = new CompanyRepository(db);
        var rows = await repo.ListAsync(spec);
        var inMemory = spec.Evaluate(rows).ToList();
        Console.WriteLine($"orphan-orderby-via-cache: orders={spec.OrderExpressions.Count()}; sqlCount={rows.Count}; memCount={inMemory.Count}");
    }

    private static async Task RunDropIncludeViaOrderBy(string connectionString)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: true);
        spec.Query.OrderBy(c => c.Name, condition: false);
        inc.ThenInclude(s => s.Products);
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
        var repo = new CompanyRepository(db);
        var rows = (await repo.ListAsync(spec)).OrderBy(c => c.Name).ToList();
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"drop-include-via-orderby: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunDropOrderByViaCache(string connectionString)
    {
        var spec = new Specification<Product>();
        var ord = spec.Query.OrderBy(p => p.Category, condition: true);
        spec.Query.EnableCache("products", condition: false);
        ord.ThenByDescending(p => p.Name);
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
        var repo = new ProductRepository(db);
        var rows = await repo.ListAsync(spec);
        var names = string.Join(", ", rows.Select(p => p.Name));
        Console.WriteLine($"drop-orderby-via-cache: orders={spec.OrderExpressions.Count()}; names=[{names}]");
    }

    private static async Task RunThreadHopDiscardedInclude(string connectionString)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: false);
        await RunOnFreshThreadAsync(() => inc.ThenInclude(s => s.Products));
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
        var repo = new CompanyRepository(db);
        var rows = (await repo.ListAsync(spec)).OrderBy(c => c.Name).ToList();
        var summary = string.Join(", ", rows.Select(c => $"{c.Name}:{c.Stores.Count}/{c.Stores.Sum(s => s.Products.Count)}"));
        Console.WriteLine($"thread-hop-discarded-include: includes={spec.IncludeExpressions.Count()}; rows=[{summary}]");
    }

    private static async Task RunThreadHopActiveInclude(string connectionString)
    {
        var spec = new Specification<Company>();
        var inc = spec.Query.Include(c => c.Stores, condition: true);
        await RunOnFreshThreadAsync(() =>
        {
            new Specification<Company>().Query.Include(c => c.Stores, condition: false);
            inc.ThenInclude(s => s.Products);
        });
        spec.Query.AsNoTracking();

        using var db = new ChainContext(connectionString);
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

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Store> Stores { get; set; } = [];
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;
}

public class ChainContext : DbContext
{
    public ChainContext(string connectionString) : base(connectionString)
    {
        Configuration.LazyLoadingEnabled = false;
        Configuration.ProxyCreationEnabled = false;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Product> Products => Set<Product>();
}

public class CompanyRepository(DbContext dbContext) : RepositoryBase<Company>(dbContext);
public class ProductRepository(DbContext dbContext) : RepositoryBase<Product>(dbContext);

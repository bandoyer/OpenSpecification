// Drives #74's AutoSaveChanges through the EF6 provider: default auto-save behaviour and deferred Unit of Work
// writes across all seven RepositoryBase<T> write methods, multi-repository commit, SQL Server constraint rollback, and
// uncommitted dispose. Seeds its own SQL Server database, prints deterministic "label: result" lines, and drops it.
using System.Data.Entity;
using System.Data.SqlClient;
using OpenSpecification;
using OpenSpecification.EntityFramework6;

const string Catalog = "VerifySpecificationAutoSaveEF6";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString();

Database.SetInitializer<AutoSaveContext>(null);
using (var db = new AutoSaveContext(connectionString))
{
    if (db.Database.Exists()) db.Database.Delete();
    db.Database.Create();
}

try
{
    await AutoSaveDriver.Run(connectionString);
}
finally
{
    SqlConnection.ClearAllPools();
    using var db = new AutoSaveContext(connectionString);
    if (db.Database.Exists()) db.Database.Delete();
}

return 0;

static class AutoSaveDriver
{
    public static async Task Run(string connectionString)
    {
        await RunDefaultWrites(connectionString);
        await ResetAsync(connectionString);
        await RunDeferredWrites(connectionString);
        await ResetAsync(connectionString);
        await RunMultiRepositoryUnitOfWork(connectionString);
        await ResetAsync(connectionString);
        await RunPartialFailureRollback(connectionString);
        await ResetAsync(connectionString);
        await RunUncommittedDispose(connectionString);
    }

    private static async Task RunDefaultWrites(string connectionString)
    {
        using var db = new AutoSaveContext(connectionString);
        var repo = new DefaultCountryRepository(db);

        var added = await repo.AddAsync(new Country { Name = "Alpha" });
        Console.WriteLine($"default-add: returned={added.Name}; db={await ReadCountriesAsync(connectionString)}");

        var range = (await repo.AddRangeAsync([new Country { Name = "Beta" }, new Country { Name = "Gamma" }])).Select(c => c.Name);
        Console.WriteLine($"default-add-range: returned=[{string.Join(", ", range)}]; db={await ReadCountriesAsync(connectionString)}");

        added.Name = "Alpha-Updated";
        var updatedCount = await repo.UpdateAsync(added);
        Console.WriteLine($"default-update: count={updatedCount}; db={await ReadCountriesAsync(connectionString)}");

        var toUpdate = await db.Countries.Where(c => c.Name == "Beta" || c.Name == "Gamma").OrderBy(c => c.Name).ToListAsync();
        foreach (var c in toUpdate) c.Name += "-Updated";
        var updatedRangeCount = await repo.UpdateRangeAsync(toUpdate);
        Console.WriteLine($"default-update-range: count={updatedRangeCount}; db={await ReadCountriesAsync(connectionString)}");

        var deletedCount = await repo.DeleteAsync(added);
        Console.WriteLine($"default-delete: count={deletedCount}; db={await ReadCountriesAsync(connectionString)}");

        var deletedRangeCount = await repo.DeleteRangeAsync([toUpdate[0]]);
        Console.WriteLine($"default-delete-range: count={deletedRangeCount}; db={await ReadCountriesAsync(connectionString)}");

        var deletedSpecCount = await repo.DeleteRangeAsync(new CountriesByNameSpec("Gamma-Updated"));
        Console.WriteLine($"default-delete-range-spec: count={deletedSpecCount}; db={await ReadCountriesAsync(connectionString)}");
    }

    private static async Task RunDeferredWrites(string connectionString)
    {
        using (var seedDb = new AutoSaveContext(connectionString))
        {
            seedDb.Countries.AddRange([
                new Country { Name = "Seed-Update" },
                new Country { Name = "Seed-UpdateRange-1" },
                new Country { Name = "Seed-UpdateRange-2" },
                new Country { Name = "Seed-Delete" },
                new Country { Name = "Seed-DeleteRange-1" },
                new Country { Name = "Seed-DeleteRange-2" },
                new Country { Name = "Seed-DeleteSpec" },
            ]);
            await seedDb.SaveChangesAsync();
        }

        using var db = new AutoSaveContext(connectionString);
        var repo = new DeferredCountryRepository(db);

        var added = await repo.AddAsync(new Country { Name = "Deferred-Add" });
        Console.WriteLine($"deferred-add-before-save: returned={added.Name}; saveCalls={repo.SaveCalls}; hasRow={await HasCountryAsync(connectionString, "Deferred-Add")}");

        var addedRange = (await repo.AddRangeAsync([new Country { Name = "Deferred-AddRange-1" }, new Country { Name = "Deferred-AddRange-2" }])).Select(c => c.Name);
        Console.WriteLine($"deferred-add-range-before-save: returned=[{string.Join(", ", addedRange)}]; saveCalls={repo.SaveCalls}; hasRow={await HasCountryAsync(connectionString, "Deferred-AddRange-1")}");

        var forUpdate = await db.Countries.SingleAsync(c => c.Name == "Seed-Update");
        forUpdate.Name = "Seed-Update-Done";
        var updateRet = await repo.UpdateAsync(forUpdate);
        Console.WriteLine($"deferred-update-before-save: returned={updateRet}; saveCalls={repo.SaveCalls}; hasUpdated={await HasCountryAsync(connectionString, "Seed-Update-Done")}");

        var forUpdateRange = await db.Countries.Where(c => c.Name.StartsWith("Seed-UpdateRange-")).OrderBy(c => c.Name).ToListAsync();
        foreach (var c in forUpdateRange) c.Name += "-Done";
        var updateRangeRet = await repo.UpdateRangeAsync(forUpdateRange);
        Console.WriteLine($"deferred-update-range-before-save: returned={updateRangeRet}; saveCalls={repo.SaveCalls}; hasUpdated={await HasCountryAsync(connectionString, "Seed-UpdateRange-1-Done")}");

        var forDelete = await db.Countries.SingleAsync(c => c.Name == "Seed-Delete");
        var deleteRet = await repo.DeleteAsync(forDelete);
        Console.WriteLine($"deferred-delete-before-save: returned={deleteRet}; saveCalls={repo.SaveCalls}; stillExists={await HasCountryAsync(connectionString, "Seed-Delete")}");

        var forDeleteRange = await db.Countries.Where(c => c.Name.StartsWith("Seed-DeleteRange-")).ToListAsync();
        var deleteRangeRet = await repo.DeleteRangeAsync(forDeleteRange);
        Console.WriteLine($"deferred-delete-range-before-save: returned={deleteRangeRet}; saveCalls={repo.SaveCalls}; stillExists={await HasCountryAsync(connectionString, "Seed-DeleteRange-1")}");

        var deleteSpecRet = await repo.DeleteRangeAsync(new CountriesByNameSpec("Seed-DeleteSpec"));
        Console.WriteLine($"deferred-delete-range-spec-before-save: returned={deleteSpecRet}; saveCalls={repo.SaveCalls}; stillExists={await HasCountryAsync(connectionString, "Seed-DeleteSpec")}");

        var savedCount = await repo.SaveChangesAsync();
        Console.WriteLine($"deferred-after-save: savedCount={savedCount}; saveCalls={repo.SaveCalls}; db={await ReadCountriesAsync(connectionString)}");
    }

    private static async Task RunMultiRepositoryUnitOfWork(string connectionString)
    {
        int existingCountryId;
        using (var seedDb = new AutoSaveContext(connectionString))
        {
            var existing = new Country { Name = "Host" };
            seedDb.Countries.Add(existing);
            await seedDb.SaveChangesAsync();
            existingCountryId = existing.Id;
        }

        using var db = new AutoSaveContext(connectionString);
        var countryRepo = new DeferredCountryRepository(db);
        var cityRepo = new DeferredCityRepository(db);

        await countryRepo.AddAsync(new Country { Name = "NewCountry" });
        await cityRepo.AddAsync(new City { Name = "NewCity", CountryId = existingCountryId });
        var host = await db.Countries.SingleAsync(c => c.Id == existingCountryId);
        host.Name = "Host-Renamed";
        await countryRepo.UpdateAsync(host);

        Console.WriteLine($"uow-before-save: countries={await ReadCountriesAsync(connectionString)}; cities={await ReadCitiesAsync(connectionString)}");
        var saved = await countryRepo.SaveChangesAsync();
        Console.WriteLine($"uow-after-save: savedCount={saved}; countries={await ReadCountriesAsync(connectionString)}; cities={await ReadCitiesAsync(connectionString)}");
    }

    private static async Task RunPartialFailureRollback(string connectionString)
    {
        using var db = new AutoSaveContext(connectionString);
        var countryRepo = new DeferredCountryRepository(db);
        var cityRepo = new DeferredCityRepository(db);

        await countryRepo.AddAsync(new Country { Name = "ShouldRollback" });
        await cityRepo.AddAsync(new City { Name = "InvalidFkCity", CountryId = -999 });

        string exceptionName;
        try
        {
            await countryRepo.SaveChangesAsync();
            exceptionName = "none";
        }
        catch (Exception ex)
        {
            exceptionName = ex.GetType().Name;
        }

        Console.WriteLine($"failure-rollback: exception={exceptionName}; countries={await ReadCountriesAsync(connectionString)}; cities={await ReadCitiesAsync(connectionString)}");
    }

    private static async Task RunUncommittedDispose(string connectionString)
    {
        using (var db = new AutoSaveContext(connectionString))
        {
            var repo = new DeferredCountryRepository(db);
            await repo.AddAsync(new Country { Name = "NeverSaved" });
        }

        Console.WriteLine($"uncommitted-dispose: countries={await ReadCountriesAsync(connectionString)}");
    }

    private static async Task ResetAsync(string connectionString)
    {
        using var db = new AutoSaveContext(connectionString);
        db.Cities.RemoveRange(db.Cities);
        db.Countries.RemoveRange(db.Countries);
        await db.SaveChangesAsync();
    }

    private static async Task<bool> HasCountryAsync(string connectionString, string name)
    {
        using var db = new AutoSaveContext(connectionString);
        return await db.Countries.AnyAsync(c => c.Name == name);
    }

    private static async Task<string> ReadCountriesAsync(string connectionString)
    {
        using var db = new AutoSaveContext(connectionString);
        var names = await db.Countries.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        return $"[{(names.Count == 0 ? "<none>" : string.Join(", ", names))}]";
    }

    private static async Task<string> ReadCitiesAsync(string connectionString)
    {
        using var db = new AutoSaveContext(connectionString);
        var names = await db.Cities.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        return $"[{(names.Count == 0 ? "<none>" : string.Join(", ", names))}]";
    }
}

public sealed class CountriesByNameSpec : Specification<Country>
{
    public CountriesByNameSpec(string name) => Query.Where(c => c.Name == name);
}

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<City> Cities { get; set; } = [];
}

public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
}

public class AutoSaveContext(string connectionString) : DbContext(connectionString)
{
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<City>()
            .HasRequired(c => c.Country)
            .WithMany(c => c.Cities)
            .HasForeignKey(c => c.CountryId);
    }
}

public class DefaultCountryRepository(DbContext dbContext) : RepositoryBase<Country>(dbContext);

public class DeferredCountryRepository(DbContext dbContext) : RepositoryBase<Country>(dbContext)
{
    public int SaveCalls { get; private set; }
    protected override bool AutoSaveChanges => false;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        return base.SaveChangesAsync(cancellationToken);
    }
}

public class DeferredCityRepository(DbContext dbContext) : RepositoryBase<City>(dbContext)
{
    protected override bool AutoSaveChanges => false;
}

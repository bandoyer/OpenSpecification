// Drives #59's paged results through the EF Core provider: PagedListAsync on RepositoryBase<T> and on
// ContextFactoryRepositoryBaseOfT, and ToPagedListAsync on a DbSet. It seeds a database of its own on SQL Server, prints
// one "label: result" line per call, and drops the database. A label that shows SQL prints each command the call sent,
// in order, on lines of their own that start "  sql: ". The recipe in features/paged-results.md copies this folder into
// an EF Core consumer in place of the template's files, and `verify run` runs it. The output holds no ids or paths.
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenSpecification;
using OpenSpecification.EntityFrameworkCore;
using OpenSpecification.Paging;

// Never the test fixtures' SpecificationTestDB_* databases, nor another consumer's.
const string Catalog = "VerifySpecificationPagedEFCore";

var server = Environment.GetEnvironmentVariable("SPECIFICATION_TEST_SQL_SERVER");
if (string.IsNullOrEmpty(server))
{
    Console.Error.WriteLine("SPECIFICATION_TEST_SQL_SERVER is unset; verify run sets it.");
    return 2;
}
var options = new DbContextOptionsBuilder<PagedContext>()
    .UseSqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = Catalog }.ToString())
    .AddInterceptors(SqlRecorder.Instance)
    .Options;

await using (var db = new PagedContext(options))
{
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    db.Countries.AddRange(Seed.Countries());
    await db.SaveChangesAsync();
}

try
{
    await Paged.Query(options);
}
finally
{
    SqlConnection.ClearAllPools();
    await using var db = new PagedContext(options);
    await db.Database.EnsureDeletedAsync();
}
return 0;

static class Seed
{
    // 25 countries named "Match 01" to "Match 25", each with two cities, among five named "Other 1" to "Other 5". The
    // matches go in out of name order, so their ids don't follow their names.
    public static IEnumerable<Country> Countries()
    {
        var order = new[] { 7, 19, 1, 25, 13, 4, 22, 10, 16, 2, 24, 8, 14, 20, 5, 11, 23, 3, 17, 9, 21, 6, 15, 12, 18 };
        for (var i = 0; i < order.Length; i++)
        {
            if (i % 5 == 0) yield return new Country { Name = $"Other {i / 5 + 1}" };
            var name = $"Match {order[i]:00}";
            yield return new Country { Name = name, Cities = [new City { Name = $"{name} a" }, new City { Name = $"{name} b" }] };
        }
    }
}

static class Paged
{
    public static async Task Query(DbContextOptions<PagedContext> options)
    {
        await using var db = new PagedContext(options);
        var repository = new CountryRepository(db);
        var factory = new Factory(options);
        var page2 = new PagingFilter { Page = 2, PageSize = 10 };

        await Print("repository-page-2", async () => Show(await repository.PagedListAsync(new Matches(), page2)));
        await Print("factory-page-2", async () => Show(await new FactoryCountryRepository(factory).PagedListAsync(new Matches(), page2)));
        await Print("dbset-page-2", async () => Show(await db.Countries.ToPagedListAsync(new Matches(), page2)));

        await Print("repository-select-page-2", async () => Show(await repository.PagedListAsync(new MatchNames(), page2)));
        await Print("factory-select-page-2", async () => Show(await new FactoryCountryRepository(factory).PagedListAsync(new MatchNames(), page2)));
        await Print("dbset-select-page-2", async () => Show(await db.Countries.ToPagedListAsync(new MatchNames(), page2)));
        // 50 cities, 12 to a page: five pages, and the last holds two.
        await Print("repository-selectmany-last", async () => Show(await repository.PagedListAsync(new MatchCities(), new PagingFilter { Page = 5, PageSize = 12 }), sortItems: true));
        await Print("dbset-selectmany-last", async () => Show(await db.Countries.ToPagedListAsync(new MatchCities(), new PagingFilter { Page = 5, PageSize = 12 }), sortItems: true));

        await Print("clamp-page-99", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = 99, PageSize = 10 })));
        await Print("clamp-page-0", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = 0, PageSize = 10 })));
        await Print("clamp-page-minus-1", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = -1, PageSize = 10 })));
        await Print("clamp-page-null", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { PageSize = 10 })));
        await Print("clamp-size-0", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = 1, PageSize = 0 })));
        await Print("clamp-size-null", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = 1 })));
        await Print("clamp-size-1000", async () => Show(await repository.PagedListAsync(new Matches(), new PagingFilter { Page = 1, PageSize = 1000 })));
        await Print("no-match", async () => Show(await repository.PagedListAsync(new NoMatch(), page2)));

        var small = new PaginationSettings(5, 20);
        await Print("settings-dbset-size-null", async () => Show(await db.Countries.ToPagedListAsync(new Matches(), new PagingFilter { Page = 1 }, small)));
        await Print("settings-dbset-size-50", async () => Show(await db.Countries.ToPagedListAsync(new Matches(), new PagingFilter { Page = 1, PageSize = 50 }, small)));
        await Print("settings-dbset-select-size-50", async () => Show(await db.Countries.ToPagedListAsync(new MatchNames(), new PagingFilter { Page = 1, PageSize = 50 }, small)));
        await Print("settings-repository-size-null", async () => Show(await new SmallPageRepository(db).PagedListAsync(new Matches(), new PagingFilter { Page = 1 })));
        await Print("settings-repository-size-50", async () => Show(await new SmallPageRepository(db).PagedListAsync(new Matches(), new PagingFilter { Page = 1, PageSize = 50 })));
        await Print("settings-invalid-0-10", () => Task.FromResult(new PaginationSettings(0, 10).ToString() ?? ""));
        await Print("settings-invalid-10-5", () => Task.FromResult(new PaginationSettings(10, 5).ToString() ?? ""));

        // A post-processed page would start "Match 20", the reverse of items 11 to 20.
        await Print("repository-include-order-post", async () => ShowWithCities(await repository.PagedListAsync(new MatchesWithCitiesReversed(), page2)), showSql: true);
        await Print("dbset-include-order-post", async () => ShowWithCities(await db.Countries.ToPagedListAsync(new MatchesWithCitiesReversed(), page2)), showSql: true);

        await Print("repository-spec-take-5", async () => Show(await repository.PagedListAsync(new MatchesTaking5(), page2)), showSql: true);
        await Print("repository-spec-skip-5", async () => Show(await repository.PagedListAsync(new MatchesSkipping5(), page2)), showSql: true);
        await Print("dbset-spec-take-5", async () => Show(await db.Countries.ToPagedListAsync(new MatchesTaking5(), page2)), showSql: true);
        await Print("factory-spec-skip-5", async () => Show(await new FactoryCountryRepository(factory).PagedListAsync(new MatchesSkipping5(), page2)), showSql: true);
        await Print("repository-null-filter", async () => Show(await repository.PagedListAsync(new Matches(), null!)), showSql: true);
        await Print("dbset-null-filter", async () => Show(await db.Countries.ToPagedListAsync(new Matches(), null!)), showSql: true);

        await Print("json", async () =>
        {
            var result = await repository.PagedListAsync(new MatchNames(), page2);
            var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
            var bySystemTextJson = JsonSerializer.Deserialize<PagedResult<string>>(json, JsonSerializerOptions.Web)!;
            var byNewtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<PagedResult<string>>(json)!;
            return $"{json}\n  system-text-json: {Show(bySystemTextJson)}\n  newtonsoft: {Show(byNewtonsoft)}";
        });
    }

    // Runs the call, then prints its result, or the exception's type, parameter and message.
    private static async Task Print(string label, Func<Task<string>> call, bool showSql = false)
    {
        SqlRecorder.Instance.Commands.Clear();
        string result;
        try
        {
            result = await call();
        }
        catch (Exception e)
        {
            result = e is ArgumentException a ? $"{e.GetType().Name} (parameter {a.ParamName ?? "<null>"}): {e.Message}" : $"{e.GetType().Name}: {e.Message}";
        }
        Console.WriteLine($"{label}: {result}");
        if (!showSql) return;
        Console.WriteLine($"  sql commands: {SqlRecorder.Instance.Commands.Count}");
        foreach (var command in SqlRecorder.Instance.Commands) Console.WriteLine($"  sql: {command}");
    }

    private static string Show<T>(PagedResult<T> result, bool sortItems = false)
    {
        var items = result.Items.Select(item => item is Country c ? c.Name : item?.ToString() ?? "<null>");
        if (sortItems) items = items.OrderBy(name => name, StringComparer.Ordinal);
        var shown = items.ToList();
        return $"[{(shown.Count == 0 ? "<none>" : string.Join(", ", shown))}] ({shown.Count} items); {Show(result.Pagination)}";
    }

    private static string ShowWithCities(PagedResult<Country> result) =>
        $"[{string.Join(", ", result.Items.Select(c => $"{c.Name} ({c.Cities.Count} cities)"))}]; {Show(result.Pagination)}";

    private static string Show(Pagination p) =>
        $"Page {p.Page}, PageSize {p.PageSize}, TotalItems {p.TotalItems}, TotalPages {p.TotalPages}, HasPrevious {p.HasPrevious.ToString().ToLowerInvariant()}, HasNext {p.HasNext.ToString().ToLowerInvariant()}";
}

public sealed class Matches : Specification<Country>
{
    public Matches() => Query.Where(c => c.Name.StartsWith("Match")).OrderBy(c => c.Name);
}

public sealed class NoMatch : Specification<Country>
{
    public NoMatch() => Query.Where(c => c.Name == "Peru").OrderBy(c => c.Name);
}

public sealed class MatchNames : Specification<Country, string>
{
    public MatchNames()
    {
        Query.Where(c => c.Name.StartsWith("Match")).OrderBy(c => c.Name);
        Query.Select(c => c.Name);
    }
}

public sealed class MatchCities : Specification<Country, string>
{
    public MatchCities()
    {
        Query.Where(c => c.Name.StartsWith("Match")).OrderBy(c => c.Name);
        Query.SelectMany(c => c.Cities.Select(city => city.Name));
    }
}

public sealed class MatchesWithCitiesReversed : Specification<Country>
{
    public MatchesWithCitiesReversed() =>
        Query.Where(c => c.Name.StartsWith("Match")).Include(c => c.Cities).OrderBy(c => c.Name).PostProcessingAction(countries => countries.Reverse());
}

public sealed class MatchesTaking5 : Specification<Country>
{
    public MatchesTaking5() => Query.Where(c => c.Name.StartsWith("Match")).OrderBy(c => c.Name).Take(5);
}

public sealed class MatchesSkipping5 : Specification<Country>
{
    public MatchesSkipping5() => Query.Where(c => c.Name.StartsWith("Match")).OrderBy(c => c.Name).Skip(5);
}

public sealed class Country
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<City> Cities { get; set; } = [];
}

public sealed class City
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int CountryId { get; set; }
}

public sealed class PagedContext(DbContextOptions<PagedContext> options) : DbContext(options)
{
    public DbSet<Country> Countries => Set<Country>();
}

public sealed class Factory(DbContextOptions<PagedContext> options) : IDbContextFactory<PagedContext>
{
    public PagedContext CreateDbContext() => new(options);
}

public sealed class CountryRepository(PagedContext context) : RepositoryBase<Country>(context);

public sealed class SmallPageRepository(PagedContext context) : RepositoryBase<Country>(context)
{
    protected override PaginationSettings PaginationSettings { get; } = new(5, 20);
}

public sealed class FactoryCountryRepository(IDbContextFactory<PagedContext> factory)
    : ContextFactoryRepositoryBaseOfT<Country, PagedContext>(factory);

// Keeps the text of each command the provider sends, on one line.
public sealed class SqlRecorder : DbCommandInterceptor
{
    public static SqlRecorder Instance { get; } = new();
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    private void Record(DbCommand command) => Commands.Add(Regex.Replace(command.CommandText, @"\s+", " ").Trim());
}

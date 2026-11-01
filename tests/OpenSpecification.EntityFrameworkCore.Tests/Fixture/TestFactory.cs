using MartinCostello.SqlLocalDb;
using Respawn;
using Testcontainers.MsSql;

namespace Tests.Fixture;

public class TestFactory : IAsyncLifetime
{
    // Flag to force using Docker SQL Server. Update it manually if you want to avoid localDb locally.
    private const bool FORCE_DOCKER = false;

    // Set it to a SQL Server connection string to run the tests against that server instead of LocalDB or Docker.
    private const string SQL_SERVER_VARIABLE = "SPECIFICATION_TEST_SQL_SERVER";

    private string _connectionString = default!;
    private Respawner _respawner = default!;
    private MsSqlContainer? _dbContainer = null;

    public DbContextOptions<TestDbContext> DbContextOptions { get; private set; } = default!;

    public Task ResetDatabase() => _respawner.ResetAsync(_connectionString);

    public async Task InitializeAsync()
    {
        var dbName = "SpecificationTestDB_EFCore";

        using (var localDB = new SqlLocalDbApi())
        {
            if (Environment.GetEnvironmentVariable(SQL_SERVER_VARIABLE) is { Length: > 0 } sqlServer)
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlServer)
                {
                    InitialCatalog = dbName
                };
                _connectionString = builder.ToString();
            }
            else if (FORCE_DOCKER || !localDB.IsLocalDBInstalled())
            {
                _dbContainer = CreateContainer();
                await _dbContainer.StartAsync();
                _connectionString = _dbContainer.GetConnectionString();
            }
            else
            {
                _connectionString = $"Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog={dbName};Integrated Security=SSPI;TrustServerCertificate=True;";
            }
        }

        Console.WriteLine($"Connection string: {_connectionString}");

        DbContextOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(_connectionString)
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging()
            .Options;

        using var dbContext = new TestDbContext(DbContextOptions);

        //await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        _respawner = await Respawner.CreateAsync(_connectionString, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            SchemasToInclude = ["dbo"],
        });

        await ResetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_dbContainer is not null)
        {
            await _dbContainer.StopAsync();
        }
        else
        {
            //using var dbContext = new TestDbContext(DbContextOptions);
            //await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static MsSqlContainer CreateContainer() => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("P@ssW0rd!")
            .Build();
}

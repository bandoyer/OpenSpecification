using System.Threading;

namespace Tests.Repositories;

[Collection("SharedCollection")]
public class Repository_AutoSaveChangesTests : IntegrationTest
{
    private readonly TestFactory _factory;

    public Repository_AutoSaveChangesTests(TestFactory factory) : base(factory)
    {
        _factory = factory;
    }

    private sealed class DeferredRepository<T>(TestDbContext context, bool autoSaveChanges = false)
        : RepositoryBase<T>(context) where T : class
    {
        public int SaveChangesCalls { get; private set; }
        public bool AutoSave { get; set; } = autoSaveChanges;
        protected override bool AutoSaveChanges => AutoSave;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<List<Country>> ReadCountriesFromFreshContextAsync()
    {
        await using var fresh = new TestDbContext(_factory.DbContextOptions);
        return await fresh.Countries.IgnoreQueryFilters().OrderBy(x => x.Name).ToListAsync();
    }

    private async Task<List<Company>> ReadCompaniesFromFreshContextAsync()
    {
        await using var fresh = new TestDbContext(_factory.DbContextOptions);
        return await fresh.Companies.OrderBy(x => x.Name).ToListAsync();
    }

    [Fact]
    public async Task AddAsync_DefersSaveChanges_WhenAutoSaveChangesIsFalse()
    {
        var repo = new DeferredRepository<Country>(DbContext);
        var country = new Country { Name = "DeferredAdd" };

        var returned = await repo.AddAsync(country);

        returned.Should().BeSameAs(country);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(1);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("DeferredAdd");
    }

    [Fact]
    public async Task AddRangeAsync_DefersSaveChanges_WhenAutoSaveChangesIsFalse()
    {
        var repo = new DeferredRepository<Country>(DbContext);
        var countries = new[]
        {
            new Country { Name = "Range1" },
            new Country { Name = "Range2" },
        };

        var returned = await repo.AddRangeAsync(countries);

        returned.Should().BeSameAs(countries);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(2);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Range1", "Range2");
    }

    [Fact]
    public async Task UpdateAsync_DefersSaveChangesAndReturnsZero_WhenAutoSaveChangesIsFalse()
    {
        await SeedAsync(new Country { Name = "Before" });
        var repo = new DeferredRepository<Country>(DbContext);
        var country = await DbContext.Countries.SingleAsync();
        country.Name = "After";

        var result = await repo.UpdateAsync(country);

        result.Should().Be(0);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Before");

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(1);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("After");
    }

    [Fact]
    public async Task UpdateRangeAsync_DefersSaveChangesAndReturnsZero_WhenAutoSaveChangesIsFalse()
    {
        await SeedRangeAsync([
            new Country { Name = "Before1" },
            new Country { Name = "Before2" },
        ]);
        var repo = new DeferredRepository<Country>(DbContext);
        var countries = await DbContext.Countries.OrderBy(x => x.Name).ToArrayAsync();
        countries[0].Name = "After1";
        countries[1].Name = "After2";

        var result = await repo.UpdateRangeAsync(countries);

        result.Should().Be(0);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Before1", "Before2");

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(2);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("After1", "After2");
    }

    [Fact]
    public async Task DeleteAsync_DefersSaveChangesAndReturnsZero_WhenAutoSaveChangesIsFalse()
    {
        await SeedAsync(new Country { Name = "ToDelete" });
        var repo = new DeferredRepository<Country>(DbContext);
        var country = await DbContext.Countries.SingleAsync();

        var result = await repo.DeleteAsync(country);

        result.Should().Be(0);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Should().ContainSingle();

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(1);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRangeAsync_Entities_DefersSaveChangesAndReturnsZero_WhenAutoSaveChangesIsFalse()
    {
        await SeedRangeAsync([
            new Country { Name = "Del1" },
            new Country { Name = "Del2" },
        ]);
        var repo = new DeferredRepository<Country>(DbContext);
        var countries = await DbContext.Countries.ToListAsync();

        var result = await repo.DeleteRangeAsync(countries);

        result.Should().Be(0);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Should().HaveCount(2);

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(2);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRangeAsync_Specification_DefersSaveChangesAndReturnsZero_WhenAutoSaveChangesIsFalse()
    {
        await SeedRangeAsync([
            new Country { Name = "Match" },
            new Country { Name = "Keep" },
        ]);
        var repo = new DeferredRepository<Country>(DbContext);
        var spec = new Specification<Country>();
        spec.Query.Where(x => x.Name == "Match");

        var result = await repo.DeleteRangeAsync(spec);

        result.Should().Be(0);
        repo.SaveChangesCalls.Should().Be(0);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Keep", "Match");

        var saved = await repo.SaveChangesAsync();

        saved.Should().Be(1);
        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Keep");
    }

    [Fact]
    public async Task MultipleRepositoriesSharingDbContext_CommitAtomicallyOnSingleSaveChanges()
    {
        var existing = new Country { Name = "Host" };
        await SeedAsync(existing);

        var countryRepo = new DeferredRepository<Country>(DbContext);
        var companyRepo = new DeferredRepository<Company>(DbContext);

        await countryRepo.AddAsync(new Country { Name = "NewCountry" });
        await companyRepo.AddAsync(new Company { Name = "NewCompany", CountryId = existing.Id });
        var host = await DbContext.Countries.SingleAsync(x => x.Id == existing.Id);
        host.Name = "HostUpdated";
        await countryRepo.UpdateAsync(host);

        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Host");
        (await ReadCompaniesFromFreshContextAsync()).Should().BeEmpty();

        var saved = await countryRepo.SaveChangesAsync();

        saved.Should().Be(3);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("HostUpdated", "NewCountry");
        (await ReadCompaniesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("NewCompany");
    }

    [Fact]
    public async Task SaveChangesAsync_RollsBackEarlierStagedWrites_WhenConstraintFails()
    {
        var countryRepo = new DeferredRepository<Country>(DbContext);
        var companyRepo = new DeferredRepository<Company>(DbContext);

        await countryRepo.AddAsync(new Country { Name = "ShouldRollback" });
        await companyRepo.AddAsync(new Company { Name = "InvalidFkCompany", CountryId = -999 });

        var act = () => countryRepo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();
        (await ReadCompaniesFromFreshContextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DisposingDbContextWithoutSaveChanges_LeavesDatabaseUnchanged()
    {
        await using (var unitOfWorkContext = new TestDbContext(_factory.DbContextOptions))
        {
            var repo = new DeferredRepository<Country>(unitOfWorkContext);
            await repo.AddAsync(new Country { Name = "NeverSaved" });
        }

        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AutoSaveChanges_CanBeToggledDynamically()
    {
        var repo = new DeferredRepository<Country>(DbContext, autoSaveChanges: false);

        await repo.AddAsync(new Country { Name = "Staged" });
        (await ReadCountriesFromFreshContextAsync()).Should().BeEmpty();

        repo.AutoSave = true;
        await repo.AddAsync(new Country { Name = "TriggersSave" });

        repo.SaveChangesCalls.Should().Be(1);
        (await ReadCountriesFromFreshContextAsync()).Select(x => x.Name).Should().Equal("Staged", "TriggersSave");
    }
}

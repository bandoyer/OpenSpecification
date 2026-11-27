using System.Runtime.CompilerServices;

namespace Tests.Evaluators;

[Collection("SharedCollection")]
public class SpecificationEvaluatorTests(TestFactory factory) : IntegrationTest(factory)
{
    private static readonly SpecificationEvaluator _evaluator = SpecificationEvaluator.Default;

    [Fact]
    public void ThrowsArgumentNullException_GivenNullSpec()
    {
        var sut = () => _evaluator.GetQuery(DbContext.Countries, (Specification<Country>)null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("specification");
    }

    [Fact]
    public void ThrowsArgumentNullException_GivenNullSpecificationWithSelector()
    {
        var sut = () => _evaluator.GetQuery(DbContext.Countries, (Specification<Country, string>)null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("specification");
    }

    [Fact]
    public void ThrowsSelectorNotFoundException_GivenNoSelector()
    {
        var spec = new Specification<Country, string>();

        var sut = () => _evaluator.GetQuery(DbContext.Countries, spec);

        sut.Should().Throw<SelectorNotFoundException>();
    }

    [Fact]
    public void Evaluate_ThrowsConcurrentSelectorsException_GivenSelectAndSelectMany()
    {
        var spec = new Specification<Company, string?>();
        spec.Query.Select(x => x.Name);
        spec.Query.SelectMany(x => x.Stores.Select(x => x.Name));

        var sut = () => _evaluator.GetQuery(DbContext.Companies, spec);

        sut.Should().Throw<ConcurrentSelectorsException>();
    }

    [Fact]
    public void GivenEmptySpec()
    {
        var spec = new Specification<Store>();

        var actual = _evaluator.GetQuery(DbContext.Stores, spec)
            .ToQueryString();

        var expected = DbContext.Stores
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenFullQuery()
    {
        var id = 2;
        var name = "Store1";
        var storeTerm = "ab1";
        var companyTerm = "ab2";
        var streetTerm = "ab3";

        var spec = new Specification<Store>();
        spec.Query
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Search(x => x.Name, $"%{storeTerm}%")
            .Search(x => x.Company.Name, $"%{companyTerm}%")
            .Search(x => x.Address.Street, $"%{streetTerm}%", 2)
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .Skip(1)
            .Take(10)
            .IgnoreQueryFilters();

        var actual = _evaluator.GetQuery(DbContext.Stores, spec)
            .ToQueryString();

        // The expression in the spec are applied in a predefined order.
        var expected = DbContext.Stores
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Where(x => EF.Functions.Like(x.Name, $"%{storeTerm}%")
                     || EF.Functions.Like(x.Company.Name, $"%{companyTerm}%"))
            .Where(x => EF.Functions.Like(x.Address.Street, $"%{streetTerm}%"))
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .IgnoreQueryFilters()
            // Pagination always applied in the end
            .Skip(1)
            .Take(10)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenExpressionsInRandomOrder()
    {
        var id = 2;
        var name = "Store1";
        var storeTerm = "ab1";
        var companyTerm = "ab2";
        var streetTerm = "ab3";

        var spec = new Specification<Store>();
        spec.Query
            .Where(x => x.Id > id)
            .Search(x => x.Name, $"%{storeTerm}%")
            .Search(x => x.Company.Name, $"%{companyTerm}%")
            .Where(x => x.Name == name)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .Search(x => x.Address.Street, $"%{streetTerm}%", 2)
            .Skip(1)
            .Take(10)
            .IgnoreQueryFilters();

        var actual = _evaluator.GetQuery(DbContext.Stores, spec)
            .ToQueryString();

        // The expression in the spec are applied in a predefined order.
        var expected = DbContext.Stores
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Where(x => EF.Functions.Like(x.Name, $"%{storeTerm}%")
                     || EF.Functions.Like(x.Company.Name, $"%{companyTerm}%"))
            .Where(x => EF.Functions.Like(x.Address.Street, $"%{streetTerm}%"))
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .IgnoreQueryFilters()
            // Pagination always applied in the end
            .Skip(1)
            .Take(10)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenFullQueryWithSelect()
    {
        var id = 2;
        var name = "Store1";
        var storeTerm = "ab1";
        var companyTerm = "ab2";
        var streetTerm = "ab3";

        var spec = new Specification<Store, string?>();
        spec.Query
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Search(x => x.Name, $"%{storeTerm}%")
            .Search(x => x.Company.Name, $"%{companyTerm}%")
            .Search(x => x.Address.Street, $"%{streetTerm}%", 2)
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .Skip(1)
            .Take(10)
            .IgnoreQueryFilters()
            .Select(x => x.Name);

        var actual = _evaluator.GetQuery(DbContext.Stores, spec)
            .ToQueryString();

        // The expression in the spec are applied in a predefined order.
        var expected = DbContext.Stores
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Where(x => EF.Functions.Like(x.Name, $"%{storeTerm}%")
                    || EF.Functions.Like(x.Company.Name, $"%{companyTerm}%"))
            .Where(x => EF.Functions.Like(x.Address.Street, $"%{streetTerm}%"))
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .IgnoreQueryFilters()
            .Select(x => x.Name)
            // Pagination always applied in the end
            .Skip(1)
            .Take(10)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    // TODO: There is a known flaw. Pagination should be applied after SelectMany [Fati, 11/03/2025]
    // It's a major breaking change, so it will be postponed to v10.
    [Fact]
    public void GivenFullQueryWithSelectMany()
    {
        var id = 2;
        var name = "Store1";
        var storeTerm = "ab1";
        var companyTerm = "ab2";
        var streetTerm = "ab3";

        var spec = new Specification<Store, string?>();
        spec.Query
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Search(x => x.Name, $"%{storeTerm}%")
            .Search(x => x.Company.Name, $"%{companyTerm}%")
            .Search(x => x.Address.Street, $"%{streetTerm}%", 2)
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .Skip(1)
            .Take(10)
            .IgnoreQueryFilters()
            .SelectMany(x => x.Products.Select(x => x.Name));

        var actual = _evaluator.GetQuery(DbContext.Stores, spec)
            .ToQueryString();

        // The expression in the spec are applied in a predefined order.
        var expected = DbContext.Stores
            .Where(x => x.Id > id)
            .Where(x => x.Name == name)
            .Where(x => EF.Functions.Like(x.Name, $"%{storeTerm}%")
                    || EF.Functions.Like(x.Company.Name, $"%{companyTerm}%"))
            .Where(x => EF.Functions.Like(x.Address.Street, $"%{streetTerm}%"))
            .Include(nameof(Address))
            .Include(x => x.Products.Where(x => x.Id > 10))
                .ThenInclude(x => x.Images)
            .Include(x => x.Company)
                .ThenInclude(x => x.Country)
            .OrderBy(x => x.Id)
                .ThenByDescending(x => x.Name)
            .Skip(1)
            .Take(10)
            .IgnoreQueryFilters()
            .SelectMany(x => x.Products.Select(x => x.Name))
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenSpecAndIgnorePagination()
    {
        var id = 2;

        var spec = new Specification<Store>();
        spec.Query
            .Where(x => x.Id > id)
            .Skip(1)
            .Take(10);

        var actual = _evaluator.GetQuery(DbContext.Stores, spec, true)
            .ToQueryString();

        var expected = DbContext.Stores
            .Where(x => x.Id > id)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenSpecWithMultipleFlags()
    {
        var spec = new Specification<Country>();
        spec.Query
            .AsTracking()
            .AsNoTrackingWithIdentityResolution()
            .AsNoTracking() // the last one overwrites other Tracking behavior.
            .IgnoreQueryFilters()
            .AsSplitQuery();

        var actual = _evaluator.GetQuery(DbContext.Countries, spec)
            .Expression
            .ToString();

        var expected = DbContext.Countries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AsSplitQuery()
            .Expression
            .ToString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void Constructor_SetsProvidedEvaluators()
    {
        var evaluators = new List<IEvaluator>
        {
            WhereEvaluator.Instance,
            OrderEvaluator.Instance,
            WhereEvaluator.Instance,
        };

        var evaluator = new SpecificationEvaluator(evaluators);

        var result = EvaluatorsOf(evaluator);
        result.Should().HaveSameCount(evaluators);
        result.Should().Equal(evaluators);
    }

    [Fact]
    public void DerivedSpecificationEvaluatorCanAlterDefaultEvaluator()
    {
        var evaluator = new SpecificationEvaluatorDerived();

        var result = EvaluatorsOf(evaluator);
        result.Should().HaveCount(16);
        result[0].Should().BeOfType<SearchEvaluator>();
        result[1].Should().BeOfType<WhereEvaluator>();
        result[2].Should().BeOfType<SearchEvaluator>();
        result[3].Should().BeOfType<DistinctEvaluator>();
        result[4].Should().BeOfType<IncludeStringEvaluator>();
        result[5].Should().BeOfType<IncludeEvaluator>();
        result[6].Should().BeOfType<OrderEvaluator>();
        result[7].Should().BeOfType<PaginationEvaluator>();
        result[8].Should().BeOfType<AsNoTrackingEvaluator>();
        result[9].Should().BeOfType<AsNoTrackingWithIdentityResolutionEvaluator>();
        result[10].Should().BeOfType<AsTrackingEvaluator>();
        result[11].Should().BeOfType<IgnoreQueryFiltersEvaluator>();
        result[12].Should().BeOfType<IgnoreAutoIncludesEvaluator>();
        result[13].Should().BeOfType<AsSplitQueryEvaluator>();
        result[14].Should().BeOfType<QueryTagEvaluator>();
        result[15].Should().BeOfType<WhereEvaluator>();
    }

    [Fact]
    public void GivenEntityDistinctQuery_AppliesDistinctBeforeOrderAndPagination()
    {
        var spec = new Specification<Store>();
        spec.Query
            .Where(x => x.Id > 0)
            .Distinct()
            .OrderBy(x => x.Name)
            .Skip(1)
            .Take(5);

        var actual = _evaluator.GetQuery(DbContext.Stores, spec).ToQueryString();
        var expected = DbContext.Stores
            .Where(x => x.Id > 0)
            .Distinct()
            .OrderBy(x => x.Name)
            .Skip(1)
            .Take(5)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenDistinctSelectProjection_AppliesDistinctBeforeSkipAndTake_AndOmitsSkipZero()
    {
        var spec = new Specification<Store, string?>();
        spec.Query
            .Where(x => x.Id > 0)
            .Distinct()
            .Skip(1)
            .Take(5)
            .Select(x => x.Name);

        var actual = _evaluator.GetQuery(DbContext.Stores, spec).ToQueryString();
        var expected = DbContext.Stores
            .Where(x => x.Id > 0)
            .Select(x => x.Name)
            .Distinct()
            .Skip(1)
            .Take(5)
            .ToQueryString();

        actual.Should().Be(expected);

        var skipZeroSpec = new Specification<Store, string?>();
        skipZeroSpec.Query
            .Where(x => x.Id > 0)
            .Distinct()
            .Skip(0)
            .Take(5)
            .Select(x => x.Name);

        var skipZeroActual = _evaluator.GetQuery(DbContext.Stores, skipZeroSpec).ToQueryString();
        var skipZeroExpected = DbContext.Stores
            .Where(x => x.Id > 0)
            .Select(x => x.Name)
            .Distinct()
            .Take(5)
            .ToQueryString();

        skipZeroActual.Should().Be(skipZeroExpected);
    }

    [Fact]
    public void GivenDistinctSelectManyProjection_AppliesDistinctBeforeSkipAndTake()
    {
        var spec = new Specification<Store, string?>();
        spec.Query
            .Where(x => x.Id > 0)
            .Distinct()
            .Skip(1)
            .Take(5)
            .SelectMany(x => x.Products.Select(p => p.Name));

        var actual = _evaluator.GetQuery(DbContext.Stores, spec).ToQueryString();
        var expected = DbContext.Stores
            .Where(x => x.Id > 0)
            .SelectMany(x => x.Products.Select(p => p.Name))
            .Distinct()
            .Skip(1)
            .Take(5)
            .ToQueryString();

        actual.Should().Be(expected);
    }

    [Fact]
    public void GivenEvaluateCriteriaOnly_AppliesEntityDistinct_AndSkipsProjectionDistinct()
    {
        var entitySpec = new Specification<Store>();
        entitySpec.Query.Where(x => x.Id > 0).Distinct();

        var projectionSpec = new Specification<Store, string?>();
        projectionSpec.Query.Where(x => x.Id > 0).Distinct().Select(x => x.Name);

        var entitySql = _evaluator.GetQuery(DbContext.Stores, entitySpec, evaluateCriteriaOnly: true).ToQueryString();
        var projectionSql = _evaluator.GetQuery(DbContext.Stores, (ISpecification<Store>)projectionSpec, evaluateCriteriaOnly: true).ToQueryString();

        entitySql.Should().Contain("DISTINCT");
        projectionSql.Should().NotContain("DISTINCT");
    }

    [Fact]
    public void GivenOrderByWithDistinctProjection_ThrowsNotSupportedException()
    {
        var selectSpec = new Specification<Store, string?>();
        selectSpec.Query.OrderBy(x => x.Name).Distinct().Select(x => x.Name);

        var selectManySpec = new Specification<Store, string?>();
        selectManySpec.Query.OrderByDescending(x => x.Name).Distinct().SelectMany(x => x.Products.Select(p => p.Name));

        var selectAction = () => _evaluator.GetQuery(DbContext.Stores, selectSpec);
        var selectManyAction = () => _evaluator.GetQuery(DbContext.Stores, selectManySpec);

        selectAction.Should().Throw<NotSupportedException>();
        selectManyAction.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void DerivedEvaluatorOverride_IsHonoredForDistinctProjection()
    {
        var evaluator = new TenantFilteringEvaluator();
        var spec = new Specification<Country, string?>();
        spec.Query.Distinct().Select(x => x.Name);

        var sql = evaluator.GetQuery(DbContext.Countries, spec).ToQueryString();

        sql.Should().Contain("DISTINCT");
        sql.Should().Contain("tenant");
    }

    private sealed class TenantFilteringEvaluator : SpecificationEvaluator
    {
        public override IQueryable<T> GetQuery<T>(IQueryable<T> query, ISpecification<T> specification, bool evaluateCriteriaOnly = false)
        {
            if (typeof(T) == typeof(Country))
            {
                query = ((IQueryable<Country>)query).Where(c => c.Name == "tenant").Cast<T>();
            }
            return base.GetQuery(query, specification, evaluateCriteriaOnly);
        }
    }

    private class SpecificationEvaluatorDerived : SpecificationEvaluator
    {
        public SpecificationEvaluatorDerived()
        {
            Evaluators.Add(WhereEvaluator.Instance);
            Evaluators.Insert(0, SearchEvaluator.Instance);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Evaluators>k__BackingField")]
    public static extern ref List<IEvaluator> EvaluatorsOf(SpecificationEvaluator @this);

}

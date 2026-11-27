namespace Tests.Evaluators;

public class SpecificationEvaluatorTests
{
    private static readonly SpecificationEvaluator _evaluator = SpecificationEvaluator.Default;

    // GetQuery checks the specification before it reads the query, so these tests need no database.
    private static readonly IQueryable<Country> _countries = new List<Country>().AsQueryable();

    [Fact]
    public void ThrowsArgumentNullException_GivenNullSpec()
    {
        var sut = () => _evaluator.GetQuery(_countries, (Specification<Country>)null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("specification");
    }

    [Fact]
    public void ThrowsArgumentNullException_GivenNullSpecificationWithSelector()
    {
        var sut = () => _evaluator.GetQuery(_countries, (Specification<Country, string>)null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("specification");
    }

    [Fact]
    public void Constructor_SetsDefaultEvaluators()
    {
        var result = new SpecificationEvaluatorDerived().EvaluatorList;

        result.Should().HaveCount(8);
        result[0].Should().BeOfType<WhereEvaluator>();
        result[1].Should().BeOfType<SearchEvaluator>();
        result[2].Should().BeOfType<DistinctEvaluator>();
        result[3].Should().BeOfType<IncludeStringEvaluator>();
        result[4].Should().BeOfType<IncludeEvaluator>();
        result[5].Should().BeOfType<OpenSpecification.EntityFramework6.OrderEvaluator>();
        result[6].Should().BeOfType<PaginationEvaluator>();
        result[7].Should().BeOfType<AsNoTrackingEvaluator>();
    }

    [Fact]
    public void GivenOrderByWithDistinctProjection_ThrowsNotSupportedException()
    {
        var selectSpec = new Specification<Country, string?>();
        selectSpec.Query.OrderBy(x => x.Name).Distinct().Select(x => x.Name);

        var selectManySpec = new Specification<Company, string?>();
        selectManySpec.Query.OrderByDescending(x => x.Name).Distinct().SelectMany(x => x.Stores.Select(s => s.Name));

        var selectAction = () => _evaluator.GetQuery(_countries, selectSpec);
        var selectManyAction = () => _evaluator.GetQuery(new List<Company>().AsQueryable(), selectManySpec);

        selectAction.Should().Throw<NotSupportedException>();
        selectManyAction.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void GivenEvaluateCriteriaOnly_AppliesEntityDistinct_AndSkipsProjectionDistinct()
    {
        var entitySpec = new Specification<Country>();
        entitySpec.Query.Distinct();

        var projectionSpec = new Specification<Country, string?>();
        projectionSpec.Query.Distinct().Select(x => x.Name);

        var entityExpr = _evaluator.GetQuery(_countries, entitySpec, evaluateCriteriaOnly: true).Expression.ToString();
        var projectionExpr = _evaluator.GetQuery(_countries, (ISpecification<Country>)projectionSpec, evaluateCriteriaOnly: true).Expression.ToString();

        entityExpr.Should().Contain(".Distinct()");
        projectionExpr.Should().NotContain(".Distinct()");
    }

    [Fact]
    public void GivenDistinctProjectionWithSkipZero_OmitsSkip()
    {
        var spec = new Specification<Country, string?>();
        spec.Query.Distinct().Skip(0).Take(3).Select(x => x.Name);

        var expr = _evaluator.GetQuery(_countries, spec).Expression.ToString();

        expr.Should().Contain(".Distinct()");
        expr.Should().NotContain(".Skip(");
        expr.Should().Contain(".Take(3)");
    }

    [Fact]
    public void DerivedEvaluatorOverride_IsHonoredForDistinctProjection()
    {
        var evaluator = new TenantFilteringEvaluator();
        var spec = new Specification<Country, string?>();
        spec.Query.Distinct().Select(x => x.Name);

        var expr = evaluator.GetQuery(_countries, spec).Expression.ToString();

        expr.Should().Contain(".Distinct()");
        expr.Should().Contain("tenant");
    }

    // Reads the protected list. The EF Core tests use UnsafeAccessor, which net472 doesn't have.
    private class SpecificationEvaluatorDerived : SpecificationEvaluator
    {
        public List<IEvaluator> EvaluatorList => Evaluators;
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
}

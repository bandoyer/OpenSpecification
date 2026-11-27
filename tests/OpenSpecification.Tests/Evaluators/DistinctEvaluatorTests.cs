namespace Tests.Evaluators;

public class DistinctEvaluatorTests
{
    private static readonly DistinctEvaluator _evaluator = DistinctEvaluator.Instance;

    public record Customer(int Id, string Name);

    [Fact]
    public void IsCriteriaEvaluator_IsTrue()
    {
        _evaluator.IsCriteriaEvaluator.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Deduplicates_GivenEntitySpecWithDistinct()
    {
        List<Customer> input =
        [
            new(1, "A"),
            new(1, "A"),
            new(2, "B"),
        ];

        var spec = new Specification<Customer>();
        spec.Query.Distinct();

        var actual = _evaluator.Evaluate(input, spec).ToList();

        actual.Should().Equal([new Customer(1, "A"), new Customer(2, "B")]);
    }

    [Fact]
    public void Evaluate_SkipsEntityDistinct_GivenProjectionSpec()
    {
        List<Customer> input =
        [
            new(1, "A"),
            new(1, "A"),
        ];

        var spec = new Specification<Customer, string>();
        spec.Query.Distinct().Select(x => x.Name);

        var actual = _evaluator.Evaluate(input, spec).ToList();

        actual.Should().HaveCount(2);
    }

    [Fact]
    public void GetQuery_AppliesDistinct_GivenEntitySpecWithDistinct()
    {
        var source = new List<Customer> { new(1, "A"), new(1, "A"), new(2, "B") }.AsQueryable();
        var spec = new Specification<Customer>();
        spec.Query.Distinct();

        var query = _evaluator.GetQuery(source, spec);

        query.Expression.ToString().Should().Contain(".Distinct()");
        query.ToList().Should().Equal([new Customer(1, "A"), new Customer(2, "B")]);
    }

    [Fact]
    public void GetQuery_SkipsEntityDistinct_GivenProjectionSpec()
    {
        var source = new List<Customer> { new(1, "A"), new(1, "A") }.AsQueryable();
        var spec = new Specification<Customer, string>();
        spec.Query.Distinct().Select(x => x.Name);

        var query = _evaluator.GetQuery(source, spec);

        query.Expression.ToString().Should().NotContain(".Distinct()");
    }

    [Fact]
    public void GetQuery_AppliesDistinct_GivenCustomEntityDistinctSpecification()
    {
        var source = new List<Customer> { new(1, "A"), new(1, "A"), new(2, "B") }.AsQueryable();
        var spec = new CustomEntityDistinctSpec(isDistinct: true);

        var query = _evaluator.GetQuery(source, spec);

        query.Expression.ToString().Should().Contain(".Distinct()");
        query.ToList().Should().Equal([new Customer(1, "A"), new Customer(2, "B")]);
    }

    [Fact]
    public void GetQuery_SkipsEntityDistinct_GivenCustomProjectionDistinctSpecification()
    {
        var source = new List<Customer> { new(1, "A"), new(1, "A") }.AsQueryable();
        var spec = new CustomProjectionDistinctSpec(isDistinct: true);

        var query = _evaluator.GetQuery(source, spec);

        query.Expression.ToString().Should().NotContain(".Distinct()");
    }

    private class CustomEntityDistinctSpec(bool isDistinct) : ISpecification<Customer>, IDistinctSpecification
    {
        public bool IsDistinct { get; } = isDistinct;
        public Dictionary<string, object> Items { get; } = [];
        public IEnumerable<WhereExpressionInfo<Customer>> WhereExpressions => [];
        public IEnumerable<OrderExpressionInfo<Customer>> OrderExpressions => [];
        public IEnumerable<IncludeExpressionInfo> IncludeExpressions => [];
        public IEnumerable<string> IncludeStrings => [];
        public IEnumerable<SearchExpressionInfo<Customer>> SearchCriterias => [];
        public int Take => -1;
        public int Skip => -1;
        public Func<IEnumerable<Customer>, IEnumerable<Customer>>? PostProcessingAction => null;
        public IEnumerable<string> QueryTags => [];
        public bool CacheEnabled => false;
        public string? CacheKey => null;
        public bool AsTracking => false;
        public bool AsNoTracking => false;
        public bool AsSplitQuery => false;
        public bool AsNoTrackingWithIdentityResolution => false;
        public bool IgnoreQueryFilters => false;
        public bool IgnoreAutoIncludes => false;
        public IEnumerable<Customer> Evaluate(IEnumerable<Customer> entities) => entities;
        public bool IsSatisfiedBy(Customer entity) => true;
    }

    private sealed class CustomProjectionDistinctSpec(bool isDistinct) : CustomEntityDistinctSpec(isDistinct), ISpecification<Customer, string>
    {
        public Expression<Func<Customer, string>>? Selector => x => x.Name;
        public Expression<Func<Customer, IEnumerable<string>>>? SelectorMany => null;
        public new Func<IEnumerable<string>, IEnumerable<string>>? PostProcessingAction => null;
        public new IEnumerable<string> Evaluate(IEnumerable<Customer> entities) => entities.Select(x => x.Name);
    }
}

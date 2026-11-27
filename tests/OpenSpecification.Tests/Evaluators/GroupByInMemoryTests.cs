namespace Tests.Evaluators;

public class GroupByInMemoryTests
{
    private record Order(int Id, string Region, string Category, int Amount, string? Tag = null);

    private sealed record CompositeKey
    {
        public string Region { get; init; } = "";
        public string Category { get; init; } = "";
    }

    private sealed record CategoryAggregate(string Category, int Count, int Sum, int Min, int Max, double Avg);

    private sealed class CustomInMemoryEvaluator(IEnumerable<IInMemoryEvaluator> evaluators)
        : InMemorySpecificationEvaluator(evaluators);

    [Fact]
    public void SingleKey_ComputesAllAggregatesWithPreGroupWhereAndSearch()
    {
        var orders = new List<Order>
        {
            new(1, "North", "Cat-A", 10, "ok"),
            new(2, "North", "Cat-A", 30, "ok"),
            new(3, "North", "Cat-B", 20, "ok"),
            new(4, "North", "Cat-B", 40, "ok"),
            new(5, "South", "Cat-A", 999, "ok"),
            new(6, "North", "Other", 500, "ok"),
        };

        var spec = new Specification<Order, CategoryAggregate>();
        spec.Query
            .Where(o => o.Region == "North")
            .Search(o => o.Category, "Cat-%")
            .Include(o => o.Tag!)
            .Include("Tag")
            .GroupBy(o => o.Category)
            .Select(g => new CategoryAggregate(
                g.Key,
                g.Count(),
                g.Sum(o => o.Amount),
                g.Min(o => o.Amount),
                g.Max(o => o.Amount),
                g.Average(o => o.Amount)));

        var result = spec.Evaluate(orders).OrderBy(x => x.Category, StringComparer.Ordinal).ToList();

        result.Should().Equal(
            new CategoryAggregate("Cat-A", 2, 40, 10, 30, 20),
            new CategoryAggregate("Cat-B", 2, 60, 20, 40, 30));
    }

    [Fact]
    public void CompositeKeys_AnonymousAndMemberInitializedRecord_GroupEquivalently()
    {
        var orders = new List<Order>
        {
            new(1, "North", "Cat-A", 10),
            new(2, "North", "Cat-A", 25),
            new(3, "South", "Cat-A", 50),
        };

        var anonSpec = new Specification<Order, string>();
        anonSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => $"{g.Key.Region}/{g.Key.Category}:{g.Count()}/{g.Sum(x => x.Amount)}");

        var recordSpec = new Specification<Order, string>();
        recordSpec.Query
            .GroupBy(o => new CompositeKey { Region = o.Region, Category = o.Category })
            .Select(g => $"{g.Key.Region}/{g.Key.Category}:{g.Count()}/{g.Sum(x => x.Amount)}");

        anonSpec.Evaluate(orders).OrderBy(x => x, StringComparer.Ordinal)
            .Should().Equal("North/Cat-A:2/35", "South/Cat-A:1/50");
        recordSpec.Evaluate(orders).OrderBy(x => x, StringComparer.Ordinal)
            .Should().Equal("North/Cat-A:2/35", "South/Cat-A:1/50");
    }

    [Fact]
    public void EmptyInput_RegularAndConstantKeys_YieldZeroGroups()
    {
        var regularSpec = new Specification<Order, int>();
        regularSpec.Query.GroupBy(o => o.Category).Select(g => g.Count());

        var constantSpec = new Specification<Order, int>();
        constantSpec.Query.GroupBy(_ => 1).Select(g => g.Count());

        regularSpec.Evaluate([]).Should().BeEmpty();
        constantSpec.Evaluate([]).Should().BeEmpty();
    }

    [Fact]
    public void InMemory_UsesDefaultEqualityComparer_OrdinalStringComparison()
    {
        var orders = new List<Order>
        {
            new(1, "North", "cat", 10),
            new(2, "North", "CAT", 20),
            new(3, "North", "cat", 30),
        };

        var spec = new Specification<Order, string>();
        spec.Query.GroupBy(o => o.Category).Select(g => $"{g.Key}:{g.Count()}");

        spec.Evaluate(orders).OrderBy(x => x, StringComparer.Ordinal)
            .Should().Equal("CAT:1", "cat:2");
    }

    [Fact]
    public void InMemory_AppliesPostProcessingAction_AndSupportsRepeatedEvaluations()
    {
        var spec = new Specification<Order, string>();
        spec.Query
            .GroupBy(o => o.Category)
            .Select(g => $"{g.Key}:{g.Count()}")
            .PostProcessingAction(items => items.OrderBy(x => x, StringComparer.Ordinal).Select(x => $"*{x}"));

        var first = spec.Evaluate([new(1, "N", "B", 1), new(2, "N", "A", 1)]).ToList();
        var second = spec.Evaluate([new(3, "N", "C", 1), new(4, "N", "C", 2)]).ToList();

        first.Should().Equal("*A:1", "*B:1");
        second.Should().Equal("*C:2");
    }

    [Fact]
    public void MissingResultSelector_ThrowsSelectorNotFoundException()
    {
        var spec = new Specification<Order, int>();
        spec.Query.GroupBy(o => o.Category);

        Action sut = () => spec.Evaluate([new(1, "N", "A", 1)]).ToList();

        sut.Should().Throw<SelectorNotFoundException>();
    }

    [Fact]
    public void MixingGroupByWithSelectOrSelectMany_ThrowsConcurrentSelectorsException()
    {
        var selectThenGroup = new Specification<Order, string>();
        selectThenGroup.Query.Select(o => o.Category);
        selectThenGroup.Query.GroupBy(o => o.Category).Select(g => g.Key);

        var groupWithoutSelectThenSelect = new Specification<Order, string>();
        groupWithoutSelectThenSelect.Query.GroupBy(o => o.Category);
        groupWithoutSelectThenSelect.Query.Select(o => o.Category);

        var groupAndSelectMany = new Specification<Order, string>();
        groupAndSelectMany.Query.GroupBy(o => o.Category).Select(g => g.Key);
        groupAndSelectMany.Query.SelectMany(o => new[] { o.Category });

        Action a1 = () => selectThenGroup.Evaluate([]).ToList();
        Action a2 = () => groupWithoutSelectThenSelect.Evaluate([]).ToList();
        Action a3 = () => groupAndSelectMany.Evaluate([]).ToList();

        a1.Should().Throw<ConcurrentSelectorsException>();
        a2.Should().Throw<ConcurrentSelectorsException>();
        a3.Should().Throw<ConcurrentSelectorsException>();
    }

    [Fact]
    public void PreGroupEntityOrderByOrSkipOrTake_ThrowsNotSupportedException()
    {
        var ordered = new Specification<Order, string>();
        ordered.Query.OrderBy(o => o.Id).GroupBy(o => o.Category).Select(g => g.Key);

        var orderedDesc = new Specification<Order, string>();
        orderedDesc.Query.OrderByDescending(o => o.Id).GroupBy(o => o.Category).Select(g => g.Key);

        var skipped = new Specification<Order, string>();
        skipped.Query.Skip(0).GroupBy(o => o.Category).Select(g => g.Key);

        var taken = new Specification<Order, string>();
        taken.Query.Take(1).GroupBy(o => o.Category).Select(g => g.Key);

        Action a1 = () => ordered.Evaluate([]).ToList();
        Action a2 = () => orderedDesc.Evaluate([]).ToList();
        Action a3 = () => skipped.Evaluate([]).ToList();
        Action a4 = () => taken.Evaluate([]).ToList();

        a1.Should().Throw<NotSupportedException>();
        a2.Should().Throw<NotSupportedException>();
        a3.Should().Throw<NotSupportedException>();
        a4.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConditionalFalseOrderByOrSkipOrTake_BeforeGroupBy_IsAllowed()
    {
        var spec = new Specification<Order, string>();
        spec.Query
            .OrderBy(o => o.Id, false)
            .Skip(5, false)
            .Take(2, false)
            .GroupBy(o => o.Category)
            .Select(g => g.Key);

        spec.Evaluate([new(1, "N", "A", 10)]).Should().Equal("A");
    }

    [Fact]
    public void InterimDistinctWithGroupBy_ThrowsNotSupportedException()
    {
        var spec = new Specification<Order, string>();
        spec.Query.Distinct().GroupBy(o => o.Category).Select(g => g.Key);

        Action sut = () => spec.Evaluate([]).ToList();

        sut.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void WithProjectionOf_ClonesGroupByPlanWithoutStateLeakage_AndFailsFastOnSourceOrderOrPaging()
    {
        var orders = new List<Order>
        {
            new(1, "North", "A", 10),
            new(2, "South", "B", 20),
        };

        var source = new Specification<Order>();
        source.Query.Where(o => o.Region == "North").Include(o => o.Tag!).Include("Tag");

        var groupedSpec = new Specification<Order, string>();
        var groupedBuilder = groupedSpec.Query.GroupBy(o => o.Category);
        groupedBuilder.Select(g => g.Key);

        var composed = source.WithProjectionOf(groupedSpec);

        // Mutating the original grouped spec after WithProjectionOf must not leak to composed.
        groupedBuilder.Select(g => $"MUTATED:{g.Key}");

        composed.Evaluate(orders).Should().Equal("A");
        groupedSpec.Evaluate(orders).OrderBy(x => x, StringComparer.Ordinal).Should().Equal("MUTATED:A", "MUTATED:B");

        var orderedSource = new Specification<Order>();
        orderedSource.Query.OrderBy(o => o.Id);
        var pagedSource = new Specification<Order>();
        pagedSource.Query.Take(1);

        Action evalOrdered = () => orderedSource.WithProjectionOf(groupedSpec).Evaluate(orders).ToList();
        Action evalPaged = () => pagedSource.WithProjectionOf(groupedSpec).Evaluate(orders).ToList();

        evalOrdered.Should().Throw<NotSupportedException>();
        evalPaged.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CustomInMemorySpecificationEvaluatorSubclass_InheritsGroupBySupport()
    {
        var customEvaluator = new CustomInMemoryEvaluator([WhereEvaluator.Instance, SearchMemoryEvaluator.Instance]);
        var spec = new Specification<Order, string>();
        spec.Query
            .Where(o => o.Region == "North")
            .GroupBy(o => o.Category)
            .Select(g => $"{g.Key}:{g.Sum(o => o.Amount)}");

        var result = customEvaluator.Evaluate([new(1, "North", "A", 10), new(2, "North", "A", 5), new(3, "South", "A", 99)], spec).ToList();

        result.Should().Equal("A:15");
    }

    [Fact]
    public void IsSatisfiedBy_RemainsEntityCriteriaOnly()
    {
        var spec = new Specification<Order, int>();
        spec.Query
            .Where(o => o.Region == "North")
            .GroupBy(o => o.Category)
            .Select(g => g.Count());

        spec.IsSatisfiedBy(new(1, "North", "A", 10)).Should().BeTrue();
        spec.IsSatisfiedBy(new(2, "South", "A", 10)).Should().BeFalse();
    }
}

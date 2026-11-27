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
    public void PostGroupDistinctAndPreGroupByDistinct_DeduplicateBeforeGroupSkipAndTake()
    {
        var orders = new List<Order>
        {
            new(1, "Alpha", "Cat-1", 10),
            new(2, "Alpha", "Cat-1", 30), // Count = 2
            new(3, "Alpha", "Cat-2", 20),
            new(4, "Alpha", "Cat-2", 40), // Count = 2
            new(5, "Beta", "Cat-1", 50),  // Count = 1
            new(6, "Beta", "Cat-2", 60),
            new(7, "Beta", "Cat-2", 80),  // Count = 2
        };

        var postDistinctSpec = new Specification<Order, int>();
        postDistinctSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => g.Count())
            .Distinct();

        var preDistinctSpec = new Specification<Order, int>();
        preDistinctSpec.Query
            .Distinct()
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => g.Count());

        var distinctFalseSpec = new Specification<Order, int>();
        distinctFalseSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => g.Count())
            .Distinct(false);

        var distinctThenTakeSpec = new Specification<Order, int>();
        distinctThenTakeSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => g.Count())
            .Distinct()
            .Take(3);

        var distinctThenSkipTakeSpec = new Specification<Order, int>();
        distinctThenSkipTakeSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Select(g => g.Count())
            .Distinct()
            .Skip(1)
            .Take(1);

        var preSkipThenDistinctSpec = new Specification<Order, int>();
        preSkipThenDistinctSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .Skip(1)
            .Take(1)
            .Select(g => g.Count())
            .Distinct();

        postDistinctSpec.Evaluate(orders).OrderBy(x => x).Should().Equal(1, 2);
        preDistinctSpec.Evaluate(orders).OrderBy(x => x).Should().Equal(1, 2);
        distinctFalseSpec.Evaluate(orders).OrderBy(x => x).Should().Equal(1, 2, 2, 2);
        // Distinct deduplicates 4 groups -> 2 values BEFORE group Skip/Take.
        distinctThenTakeSpec.Evaluate(orders).Should().HaveCount(2);
        distinctThenSkipTakeSpec.Evaluate(orders).Should().Equal(1);
        preSkipThenDistinctSpec.Evaluate(orders).Should().Equal(1);
    }

    [Fact]
    public void GroupSkipAndTake_SupportSkipZeroAndTakeZero()
    {
        var orders = new List<Order>
        {
            new(1, "Alpha", "Cat-A", 10),
            new(2, "Alpha", "Cat-B", 20),
            new(3, "Alpha", "Cat-C", 30),
        };

        var skipZeroSpec = new Specification<Order, string>();
        skipZeroSpec.Query.GroupBy(o => o.Category).OrderBy(g => g.Key).Skip(0).Select(g => g.Key);

        var takeZeroSpec = new Specification<Order, string>();
        takeZeroSpec.Query.GroupBy(o => o.Category).OrderBy(g => g.Key).Select(g => g.Key).Take(0);

        skipZeroSpec.Evaluate(orders).Should().Equal("Cat-A", "Cat-B", "Cat-C");
        takeZeroSpec.Evaluate(orders).Should().BeEmpty();
    }

    [Fact]
    public void GroupOrderByWithDistinct_ThrowsNotSupportedException_UnlessOrderByIsInactive()
    {
        var orders = new List<Order>
        {
            new(1, "Alpha", "Cat-1", 10),
            new(2, "Alpha", "Cat-1", 30),
            new(3, "Beta", "Cat-2", 50),
        };

        var postDistinctOrdered = new Specification<Order, int>();
        postDistinctOrdered.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key)
            .Select(g => g.Count())
            .Distinct();

        var preDistinctOrdered = new Specification<Order, int>();
        preDistinctOrdered.Query
            .Distinct()
            .GroupBy(o => o.Category)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Count());

        var inactiveOrderDistinct = new Specification<Order, int>();
        inactiveOrderDistinct.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key, false)
            .Select(g => g.Count())
            .Distinct();

        Action a1 = () => postDistinctOrdered.Evaluate(orders).ToList();
        Action a2 = () => preDistinctOrdered.Evaluate(orders).ToList();

        a1.Should().Throw<NotSupportedException>();
        a2.Should().Throw<NotSupportedException>();
        inactiveOrderDistinct.Evaluate(orders).OrderBy(x => x).Should().Equal(1, 2);
    }

    [Fact]
    public void WithProjectionOf_ClonesGroupSkipTakeAndIsDistinctWithoutStateLeakage()
    {
        var orders = new List<Order>
        {
            new(1, "North", "Cat-A", 10),
            new(2, "North", "Cat-B", 20),
            new(3, "North", "Cat-C", 30),
            new(4, "South", "Cat-D", 40),
        };

        var source = new Specification<Order>();
        source.Query.Where(o => o.Region == "North");

        var sliceTemplate = new Specification<Order, string>();
        var sliceResult = sliceTemplate.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key)
            .Skip(1)
            .Select(g => g.Key);

        var composedSlice = source.WithProjectionOf(sliceTemplate);
        sliceResult.Take(1); // Mutating template after cloning must not leak into composed.

        composedSlice.Evaluate(orders).Should().Equal("Cat-B", "Cat-C");
        sliceTemplate.Evaluate(orders).Should().Equal("Cat-B");

        var distinctTemplate = new Specification<Order, int>();
        distinctTemplate.Query
            .GroupBy(o => o.Category)
            .Select(g => g.Count())
            .Distinct();

        var composedDistinct = source.WithProjectionOf(distinctTemplate);
        composedDistinct.IsDistinct.Should().BeTrue();
        composedDistinct.Evaluate(orders).Should().Equal(1);
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

    [Fact]
    public void Having_MultiplePredicatesCombineWithAnd_AndFalseConditionIsNoOpWithoutDiscardingSubsequentClauses()
    {
        var orders = new List<Order>
        {
            new(1, "North", "Cat-A", 10),
            new(2, "North", "Cat-A", 20), // Count=2, Sum=30
            new(3, "North", "Cat-B", 25),
            new(4, "North", "Cat-B", 35), // Count=2, Sum=60
            new(5, "North", "Cat-C", 90), // Count=1, Sum=90
        };

        var spec = new Specification<Order, string>();
        spec.Query
            .GroupBy(o => o.Category)
            .Having(g => g.Count() >= 2)
            .Having(g => g.Count() > 99, condition: false)
            .Having(g => g.Sum(o => o.Amount) >= 50)
            .Select(g => $"{g.Key}:{g.Count()}/{g.Sum(o => o.Amount)}");

        spec.Evaluate(orders).Should().Equal("Cat-B:2/60");

        // Single-Having fast path (OneOrMany single item).
        var singleHavingSpec = new Specification<Order, string>();
        singleHavingSpec.Query
            .GroupBy(o => o.Category)
            .Having(g => g.Sum(o => o.Amount) >= 60)
            .OrderByDescending(g => g.Sum(o => o.Amount))
            .Select(g => g.Key);

        singleHavingSpec.Evaluate(orders).Should().Equal("Cat-C", "Cat-B");

        // OrderByDescending(false).ThenByDescending(...) discards continuation without affecting sibling chain.
        var descDiscardSpec = new Specification<Order, string>();
        descDiscardSpec.Query
            .GroupBy(o => o.Category)
            .OrderByDescending(g => g.Count(), false)
            .ThenByDescending(g => g.Key)
            .OrderBy(g => g.Key, true)
            .Select(g => g.Key);

        descDiscardSpec.Evaluate(orders).Should().Equal("Cat-A", "Cat-B", "Cat-C");
    }

    [Fact]
    public void OrderByAndThenBy_SortByGroupKeyCompositeMembersAndUnprojectedValueTypeAggregates()
    {
        var orders = new List<Order>
        {
            new(1, "Alpha", "Cat-1", 10),
            new(2, "Alpha", "Cat-1", 30), // Count=2, Sum=40, Max=30, Avg=20
            new(3, "Alpha", "Cat-2", 20),
            new(4, "Alpha", "Cat-2", 40), // Count=2, Sum=60, Max=40, Avg=30
            new(5, "Beta", "Cat-1", 50),  // Count=1, Sum=50, Max=50, Avg=50
            new(6, "Beta", "Cat-2", 60),
            new(7, "Beta", "Cat-2", 80),  // Count=2, Sum=140, Max=80, Avg=70
        };

        var multiLevelSpec = new Specification<Order, string>();
        multiLevelSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Region)
            .ThenByDescending(g => g.Sum(o => o.Amount))
            .Select(g => $"{g.Key.Region}/{g.Key.Category}");

        var avgSpec = new Specification<Order, string>();
        avgSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .OrderBy(g => g.Average(o => o.Amount))
            .Select(g => $"{g.Key.Region}/{g.Key.Category}");

        var orderThenHavingSpec = new Specification<Order, string>();
        orderThenHavingSpec.Query
            .GroupBy(o => new { o.Region, o.Category })
            .OrderByDescending(g => g.Max(o => o.Amount))
            .Having(g => g.Count() >= 2)
            .Having(g => g.Sum(o => o.Amount) >= 50)
            .Select(g => $"{g.Key.Region}/{g.Key.Category}");

        multiLevelSpec.Evaluate(orders).Should().Equal("Alpha/Cat-2", "Alpha/Cat-1", "Beta/Cat-2", "Beta/Cat-1");
        // Repeated evaluation reuses lazily compiled delegates cleanly.
        multiLevelSpec.Evaluate(orders).Should().Equal("Alpha/Cat-2", "Alpha/Cat-1", "Beta/Cat-2", "Beta/Cat-1");
        avgSpec.Evaluate(orders).Should().Equal("Alpha/Cat-1", "Alpha/Cat-2", "Beta/Cat-1", "Beta/Cat-2");
        orderThenHavingSpec.Evaluate(orders).Should().Equal("Beta/Cat-2", "Alpha/Cat-2");
        multiLevelSpec.Evaluate([]).Should().BeEmpty();
    }

    [Fact]
    public void DuplicatePrimaryGroupOrderChains_ThrowDuplicateOrderChainExceptionAtEvaluationTime()
    {
        var orders = new List<Order> { new(1, "Alpha", "Cat-1", 10), new(2, "Beta", "Cat-2", 20) };

        var directDup = new Specification<Order, string>();
        directDup.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var acrossHavingDup = new Specification<Order, string>();
        acrossHavingDup.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key)
            .Having(g => g.Count() > 0)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key);

        var oneActiveOneFalse = new Specification<Order, string>();
        oneActiveOneFalse.Query
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key, true)
            .OrderByDescending(g => g.Count(), false)
            .Select(g => g.Key);

        Action a1 = () => directDup.Evaluate(orders);
        Action a2 = () => acrossHavingDup.Evaluate(orders);

        a1.Should().Throw<DuplicateOrderChainException>();
        a2.Should().Throw<DuplicateOrderChainException>();
        oneActiveOneFalse.Evaluate(orders).Should().Equal("Cat-1", "Cat-2");
    }

    [Fact]
    public void WithProjectionOf_ClonesHavingAndOrderStepsWithoutStateLeakage()
    {
        var orders = new List<Order>
        {
            new(1, "North", "Cat-A", 10),
            new(2, "North", "Cat-A", 20),
            new(3, "North", "Cat-B", 30),
            new(4, "North", "Cat-B", 40),
            new(5, "North", "Cat-B", 50),
            new(6, "South", "Cat-C", 99),
        };

        var source = new Specification<Order>();
        source.Query.Where(o => o.Region == "North");

        var template = new Specification<Order, string>();
        var templateGrouped = template.Query
            .GroupBy(o => o.Category)
            .Having(g => g.Count() >= 2)
            .Having(g => g.Sum(o => o.Amount) >= 30);
        var templateOrdered = templateGrouped
            .OrderBy(g => g.Count())
            .ThenBy(g => g.Key);
        templateOrdered.Select(g => g.Key);

        var composed = source.WithProjectionOf(template);

        // Mutating template's Having predicates and Order steps after cloning must not leak into composed.
        templateGrouped.Having(g => g.Count() >= 3);
        templateOrdered.ThenByDescending(g => g.Count());
        templateGrouped.OrderByDescending(g => g.Sum(o => o.Amount));

        composed.Evaluate(orders).Should().Equal("Cat-A", "Cat-B");
        Action evalMutatedTemplate = () => template.Evaluate(orders);
        evalMutatedTemplate.Should().Throw<DuplicateOrderChainException>();
    }
}

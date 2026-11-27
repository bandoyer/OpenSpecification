using System.Threading.Tasks;

namespace Tests.Builders;

public class Builder_GroupBy
{
    public record Customer(int Id, string City, int Spend);

    [Fact]
    public void GroupBy_ReturnsIsolatedGroupedBuilder()
    {
        var spec = new Specification<Customer, int>();

        var grouped = spec.Query.GroupBy(c => c.City);
        var result = grouped.Select(g => g.Count());

        grouped.Specification.Should().BeSameAs(spec);
        result.Specification.Should().BeSameAs(spec);
        grouped.Should().NotBeAssignableTo<ISpecificationBuilder<Customer, int>>();
        result.Should().NotBeAssignableTo<ISpecificationBuilder<Customer, int>>();
    }

    [Fact]
    public void GroupBy_ThrowsDuplicateGroupByException_WhenCalledTwice_AndPreservesFirstPlan()
    {
        var spec = new Specification<Customer, string>();
        spec.Query.GroupBy(c => c.City).Select(g => g.Key);

        Action secondCall = () => spec.Query.GroupBy(c => c.Id);

        secondCall.Should().Throw<DuplicateGroupByException>();

        var evaluated = spec.Evaluate([new(1, "Austin", 10), new(2, "Austin", 20)]).ToList();
        evaluated.Should().Equal("Austin");
    }

    [Fact]
    public void GroupBy_ThrowsArgumentNullException_GivenNullKeySelector()
    {
        var spec = new Specification<Customer, int>();

        Action sut = () => spec.Query.GroupBy<Customer, string, int>(null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
    }

    [Fact]
    public void GroupedSelect_ThrowsArgumentNullException_GivenNullSelector()
    {
        var spec = new Specification<Customer, int>();
        var grouped = spec.Query.GroupBy(c => c.City);

        Action sut = () => grouped.Select(null!);

        sut.Should().Throw<ArgumentNullException>().WithParameterName("selector");
    }

    [Fact]
    public void PostProcessingAction_SetsSpecificationPostProcessingAction()
    {
        var spec = new Specification<Customer, int>();
        Func<IEnumerable<int>, IEnumerable<int>> action = items => items.Select(x => x * 2);

        var result = spec.Query
            .GroupBy(c => c.City)
            .Select(g => g.Count())
            .PostProcessingAction(action);

        result.Specification.Should().BeSameAs(spec);
        spec.PostProcessingAction.Should().BeSameAs(action);
    }

    [Fact]
    public void EnableCache_SetsCacheKeyIdenticallyToStandardBuilder()
    {
        var spec = new Specification<Customer, int>();

        spec.Query
            .GroupBy(c => c.City)
            .Select(g => g.Count())
            .EnableCache("GroupedCustomers", "US", 42);

        spec.CacheEnabled.Should().BeTrue();
        spec.CacheKey.Should().Be("GroupedCustomers-US-42");
    }

    [Fact]
    public void EnableCache_WithFalseCondition_DoesNotSetCacheKey()
    {
        var spec = new Specification<Customer, int>();

        spec.Query
            .GroupBy(c => c.City)
            .Select(g => g.Count())
            .EnableCache("GroupedCustomers", false, "US");

        spec.CacheEnabled.Should().BeFalse();
        spec.CacheKey.Should().BeNull();
    }

    [Fact]
    public void EnableCache_WithNullOrEmptyNameWhenActive_ThrowsArgumentException()
    {
        var spec = new Specification<Customer, int>();
        var result = spec.Query.GroupBy(c => c.City).Select(g => g.Count());

        Action emptyCall = () => result.EnableCache("");
        Action nullCall = () => result.EnableCache(null!);

        emptyCall.Should().Throw<ArgumentException>();
        nullCall.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithCacheKey_SetsCacheKeyIdenticallyToStandardBuilder()
    {
        var spec = new Specification<Customer, int>();

        spec.Query
            .GroupBy(c => c.City)
            .Select(g => g.Count())
            .WithCacheKey("custom-grouped-key");

        spec.CacheEnabled.Should().BeTrue();
        spec.CacheKey.Should().Be("custom-grouped-key");
    }

    [Fact]
    public void WithCacheKey_WithFalseCondition_DoesNotSetCacheKey()
    {
        var spec = new Specification<Customer, int>();

        spec.Query
            .GroupBy(c => c.City)
            .Select(g => g.Count())
            .WithCacheKey("custom-grouped-key", false);

        spec.CacheEnabled.Should().BeFalse();
        spec.CacheKey.Should().BeNull();
    }

    [Fact]
    public void WithCacheKey_WithNullOrEmptyKeyWhenActive_ThrowsArgumentException()
    {
        var spec = new Specification<Customer, int>();
        var result = spec.Query.GroupBy(c => c.City).Select(g => g.Count());

        Action emptyCall = () => result.WithCacheKey("");
        Action nullCall = () => result.WithCacheKey(null!);

        emptyCall.Should().Throw<ArgumentException>();
        nullCall.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Having_ThrowsArgumentNullException_GivenNullBuilderOrActiveNullPredicate()
    {
        IGroupedSpecificationBuilder<Customer, string, int> nullBuilder = null!;
        var spec = new Specification<Customer, int>();
        var grouped = spec.Query.GroupBy(c => c.City);

        Action nullBuilderCall = () => nullBuilder.Having(g => g.Count() > 1);
        Action nullPredicateCall = () => grouped.Having(null!);
        Action activeNullPredicateCall = () => grouped.Having(null!, true);
        Action inactiveNullPredicateCall = () => grouped.Having(null!, false);

        nullBuilderCall.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        nullPredicateCall.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
        activeNullPredicateCall.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
        inactiveNullPredicateCall.Should().NotThrow();
    }

    [Fact]
    public void OrderBy_ReturnsIsolatedOrderedGroupedBuilder_AndHavingReturnsNonOrderedGroupedBuilder()
    {
        var spec = new Specification<Customer, string>();

        var grouped = spec.Query.GroupBy(c => c.City);
        var ordered = grouped.OrderBy(g => g.Key);
        var afterHaving = ordered.Having(g => g.Count() > 0);
        var afterInactiveHaving = ordered.Having(g => g.Count() > 99, false);

        ordered.Specification.Should().BeSameAs(spec);
        ordered.Should().BeAssignableTo<IOrderedGroupedSpecificationBuilder<Customer, string, string>>();
        ordered.Should().BeAssignableTo<IGroupedSpecificationBuilder<Customer, string, string>>();
        ordered.Should().NotBeAssignableTo<ISpecificationBuilder<Customer, string>>();

        afterHaving.Should().NotBeAssignableTo<IOrderedGroupedSpecificationBuilder<Customer, string, string>>();
        afterInactiveHaving.Should().NotBeAssignableTo<IOrderedGroupedSpecificationBuilder<Customer, string, string>>();
    }

    [Fact]
    public void OrderAndThenBy_ThrowArgumentNullException_GivenNullBuilderOrActiveNullSelector()
    {
        IGroupedSpecificationBuilder<Customer, string, int> nullGrouped = null!;
        IOrderedGroupedSpecificationBuilder<Customer, string, int> nullOrdered = null!;
        var spec = new Specification<Customer, int>();
        var grouped = spec.Query.GroupBy(c => c.City);
        var ordered = grouped.OrderBy(g => g.Key);
        var discarded = grouped.OrderBy(g => g.Key, false);

        Action a1 = () => nullGrouped.OrderBy(g => g.Key);
        Action a2 = () => nullGrouped.OrderByDescending(g => g.Key);
        Action a3 = () => nullOrdered.ThenBy(g => g.Key);
        Action a4 = () => nullOrdered.ThenByDescending(g => g.Key);
        Action a5 = () => grouped.OrderBy<Customer, string, int, string>(null!);
        Action a6 = () => grouped.OrderByDescending<Customer, string, int, string>(null!, true);
        Action a7 = () => ordered.ThenBy<Customer, string, int, int>(null!);
        Action a8 = () => ordered.ThenByDescending<Customer, string, int, int>(null!, true);

        Action inactiveOrder = () => grouped.OrderBy<Customer, string, int, string>(null!, false);
        Action inactiveThen = () => ordered.ThenBy<Customer, string, int, int>(null!, false);
        Action discardedThen = () => discarded.ThenBy<Customer, string, int, int>(null!, true);

        a1.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        a2.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        a3.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        a4.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        a5.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
        a6.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
        a7.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
        a8.Should().Throw<ArgumentNullException>().WithParameterName("keySelector");
        inactiveOrder.Should().NotThrow();
        inactiveThen.Should().NotThrow();
        discardedThen.Should().NotThrow();
    }

    [Fact]
    public async Task GroupedOrderChains_IsolateDiscardStateAcrossSavedHandlesSiblingSpecsAndThreads()
    {
        var customers = new List<Customer>
        {
            new(1, "B", 10),
            new(2, "B", 10), // Count=2, Sum=20
            new(3, "A", 15),
            new(4, "A", 25), // Count=2, Sum=40
            new(5, "C", 50), // Count=1, Sum=50
        };

        // 1. Discarded primary OrderBy discards its ThenBy chain; sibling active OrderBy works.
        var spec1 = new Specification<Customer, string>();
        var g1 = spec1.Query.GroupBy(c => c.City);
        var discardedChain = g1.OrderBy(g => g.Key, false);
        var activeChain = g1.OrderByDescending(g => g.Count(), true);
        await RunOnFreshThreadAsync(() => discardedChain.ThenByDescending(g => g.Sum(c => c.Spend)));
        await RunOnFreshThreadAsync(() => activeChain.ThenByDescending(g => g.Sum(c => c.Spend)));
        g1.Select(g => g.Key);

        spec1.Evaluate(customers).Should().Equal("A", "B", "C");

        // 2. Nested ThenBy(false) discards subsequent ThenBy on that branch, while saved handle stays active.
        var spec2 = new Specification<Customer, string>();
        var activeRoot = spec2.Query.GroupBy(c => c.City).OrderByDescending(g => g.Count());
        var deadBranch = activeRoot.ThenBy(g => g.Key, false);
        deadBranch.ThenByDescending(g => g.Sum(c => c.Spend)); // discarded
        activeRoot.ThenBy(g => g.Sum(c => c.Spend)); // active: Sum 20 (B) before Sum 40 (A)
        activeRoot.Select(g => g.Key);

        spec2.Evaluate(customers).Should().Equal("B", "A", "C");

        // 3. Separate specification's discarded chain does not leak into another specification's active chain.
        var spec3 = new Specification<Customer, string>();
        var spec4 = new Specification<Customer, string>();
        var active3 = spec3.Query.GroupBy(c => c.City).OrderByDescending(g => g.Count());
        spec4.Query.GroupBy(c => c.City).OrderBy(g => g.Key, false).ThenBy(g => g.Count());
        active3.ThenBy(g => g.Key).Select(g => g.Key);

        spec3.Evaluate(customers).Should().Equal("A", "B", "C");
    }

    private static Task RunOnFreshThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.Start();
        return tcs.Task;
    }
}

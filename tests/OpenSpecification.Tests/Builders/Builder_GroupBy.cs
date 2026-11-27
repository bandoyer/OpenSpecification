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
}

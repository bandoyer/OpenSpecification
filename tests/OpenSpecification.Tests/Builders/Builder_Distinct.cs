namespace Tests.Builders;

public class Builder_Distinct
{
    public record Customer(int Id, string Name);

    [Fact]
    public void DoesNothing_GivenNoDistinct()
    {
        var spec1 = new Specification<Customer>();
        var spec2 = new Specification<Customer, string>();

        spec1.IsDistinct.Should().BeFalse();
        spec2.IsDistinct.Should().BeFalse();
        ((IDistinctSpecification)spec1).IsDistinct.Should().BeFalse();
        ((IDistinctSpecification)spec2).IsDistinct.Should().BeFalse();
    }

    [Fact]
    public void DoesNothing_GivenDistinctWithFalseCondition()
    {
        var spec1 = new Specification<Customer>();
        spec1.Query.Distinct(false);

        var spec2 = new Specification<Customer, string>();
        spec2.Query.Distinct(false);

        spec1.IsDistinct.Should().BeFalse();
        spec2.IsDistinct.Should().BeFalse();
    }

    [Fact]
    public void SetsIsDistinct_GivenDistinct()
    {
        var spec1 = new Specification<Customer>();
        spec1.Query.Distinct();

        var spec2 = new Specification<Customer, string>();
        spec2.Query.Distinct();

        spec1.IsDistinct.Should().BeTrue();
        spec2.IsDistinct.Should().BeTrue();
    }

    [Fact]
    public void DoesNotClearIsDistinct_GivenTrueThenFalseCondition()
    {
        var spec1 = new Specification<Customer>();
        spec1.Query.Distinct(true).Distinct(false);

        var spec2 = new Specification<Customer, string>();
        spec2.Query.Distinct(true).Distinct(false);

        spec1.IsDistinct.Should().BeTrue();
        spec2.IsDistinct.Should().BeTrue();
    }

    [Fact]
    public void WorksUnconstrainedOnValueTypes()
    {
        var entitySpec = new Specification<int>();
        entitySpec.Query.Distinct();

        var projectionSpec = new Specification<int, string>();
        projectionSpec.Query.Distinct().Select(x => x.ToString());

        entitySpec.IsDistinct.Should().BeTrue();
        projectionSpec.IsDistinct.Should().BeTrue();
    }
}

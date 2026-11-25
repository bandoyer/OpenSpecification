using System.Threading;
using System.Threading.Tasks;

namespace Tests.Builders;

public class Builder_ChainIsolationTests
{
    public record Customer(int Id, string Name, Address Address, List<Address> Addresses);
    public record Address(int Id, Contact Contact, List<Contact> Contacts);
    public record Contact(int Id, Phone Phone);
    public record Phone(int Id, string Number);

    [Fact]
    public void Control_LinearFluentChainsWork()
    {
        var s1 = new Specification<Customer>();
        s1.Query.Include(c => c.Address).ThenInclude(a => a.Contact);

        var s2 = new Specification<Customer>();
        s2.Query.Include(c => c.Address, false).ThenInclude(a => a.Contact).ThenInclude(ct => ct.Phone);

        var s3 = new Specification<Customer>();
        s3.Query.Include(c => c.Address).ThenInclude(a => a.Contact, false).ThenInclude(ct => ct.Phone);

        var s4 = new Specification<Customer>();
        s4.Query.OrderBy(c => c.Name).ThenBy(c => c.Id);

        var s5 = new Specification<Customer>();
        s5.Query.OrderBy(c => c.Name, false).ThenBy(c => c.Id);

        var s6 = new Specification<Customer>();
        s6.Query.OrderBy(c => c.Name).ThenBy(c => c.Id, false).ThenByDescending(c => c.Name);

        var s7 = new Specification<Customer, string>();
        s7.Query.OrderBy(c => c.Name).ThenBy(c => c.Id, false).ThenBy(c => c.Name);

        s1.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
        s2.IncludeExpressions.Should().BeEmpty();
        s3.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include);
        s4.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenBy);
        s5.OrderExpressions.Should().BeEmpty();
        s6.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy);
        s7.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy);
    }

    [Fact]
    public void Control_IsChainDiscardedFieldRemainsPublicStaticBool()
    {
        var field = typeof(Specification<Customer>).GetField("IsChainDiscarded");

        field.Should().NotBeNull();
        field!.IsPublic.Should().BeTrue();
        field.IsStatic.Should().BeTrue();
        field.FieldType.Should().Be(typeof(bool));
    }

    [Fact]
    public void DoesNotEmitOrphanThenInclude_WhenOrderByFollowsDiscardedInclude()
    {
        var spec = new Specification<Customer>();
        var includeChain = spec.Query.Include(c => c.Address, condition: false);
        spec.Query.OrderBy(c => c.Id, condition: true);
        includeChain.ThenInclude(a => a.Contact);

        var specProj = new Specification<Customer, string>();
        var includeProj = specProj.Query.Include(c => c.Addresses, condition: false);
        specProj.Query.OrderByDescending(c => c.Id, condition: true);
        includeProj.ThenInclude(a => a.Contact);

        spec.IncludeExpressions.Should().BeEmpty();
        specProj.IncludeExpressions.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotEmitOrphanThenBy_WhenIncludeFollowsDiscardedOrderBy()
    {
        var spec = new Specification<Customer>();
        var orderChain = spec.Query.OrderBy(c => c.Name, condition: false);
        spec.Query.Include(c => c.Address, condition: true);
        orderChain.ThenBy(c => c.Id);

        var specProj = new Specification<Customer, string>();
        var orderProj = specProj.Query.OrderByDescending(c => c.Name, condition: false);
        specProj.Query.Include(c => c.Address, condition: true);
        orderProj.ThenByDescending(c => c.Id);

        spec.OrderExpressions.Should().BeEmpty();
        specProj.OrderExpressions.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotEmitOrphanThenByOrCrashEvaluate_WhenCacheCallFollowsDiscardedOrderBy()
    {
        var customers = new[]
        {
            new Customer(2, "B", new Address(1, new Contact(1, new Phone(1, "111")), []), []),
            new Customer(1, "A", new Address(2, new Contact(2, new Phone(2, "222")), []), []),
        };

        var spec1 = new Specification<Customer>();
        var orderChain1 = spec1.Query.OrderBy(c => c.Name, condition: false);
        spec1.Query.EnableCache("customers", condition: true);
        orderChain1.ThenBy(c => c.Id).ThenBy(c => c.Name);

        var result1 = spec1.Evaluate(customers).ToList();

        var spec2 = new Specification<Customer>();
        var orderChain2 = spec2.Query.OrderBy(c => c.Name, condition: false);
        spec2.Query.WithCacheKey("customers-key", condition: true);
        orderChain2.ThenByDescending(c => c.Id).ThenBy(c => c.Name);

        var result2 = spec2.Evaluate(customers).ToList();

        spec1.OrderExpressions.Should().BeEmpty();
        result1.Should().HaveCount(2);
        spec2.OrderExpressions.Should().BeEmpty();
        result2.Should().HaveCount(2);
    }

    [Fact]
    public void DoesNotEmitOrphanThenInclude_WhenCacheCallFollowsDiscardedInclude()
    {
        var spec1 = new Specification<Customer>();
        var inc1 = spec1.Query.Include(c => c.Address, condition: false);
        spec1.Query.EnableCache("key", condition: true);
        inc1.ThenInclude(a => a.Contact);

        var spec2 = new Specification<Customer>();
        var inc2 = spec2.Query.Include(c => c.Address, condition: false);
        spec2.Query.WithCacheKey("key", condition: true);
        inc2.ThenInclude(a => a.Contact);

        spec1.IncludeExpressions.Should().BeEmpty();
        spec2.IncludeExpressions.Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNotEmitOrphanThenInclude_AcrossThreadHop()
    {
        var spec = new Specification<Customer>();
        var includeChain = spec.Query.Include(c => c.Address, condition: false);
        await RunOnFreshThreadAsync(() => includeChain.ThenInclude(a => a.Contact));

        spec.IncludeExpressions.Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNotEmitOrphanThenBy_AcrossThreadHop()
    {
        var spec = new Specification<Customer>();
        var orderChain = spec.Query.OrderBy(c => c.Name, condition: false);
        await RunOnFreshThreadAsync(() => orderChain.ThenBy(c => c.Id));

        spec.OrderExpressions.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotEmitOrphanThenInclude_AcrossTwoSpecifications()
    {
        var spec1 = new Specification<Customer>();
        var spec2 = new Specification<Customer>();
        var inc1 = spec1.Query.Include(c => c.Address, condition: false);
        spec2.Query.Include(c => c.Address, condition: true);
        inc1.ThenInclude(a => a.Contact);

        spec1.IncludeExpressions.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotDropActiveThenInclude_WhenDiscardedOrderByIntervenes()
    {
        var spec = new Specification<Customer>();
        var includeChain = spec.Query.Include(c => c.Address, condition: true);
        spec.Query.OrderBy(c => c.Id, condition: false);
        includeChain.ThenInclude(a => a.Contact);

        var specProj = new Specification<Customer, string>();
        var includeProj = specProj.Query.Include(c => c.Addresses, condition: true);
        specProj.Query.OrderByDescending(c => c.Id, condition: false);
        includeProj.ThenInclude(a => a.Contact);

        spec.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
        specProj.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
    }

    [Fact]
    public void DoesNotDropActiveThenBy_WhenDiscardedIncludeIntervenes()
    {
        var spec = new Specification<Customer>();
        var orderChain = spec.Query.OrderBy(c => c.Name, condition: true);
        spec.Query.Include(c => c.Address, condition: false);
        orderChain.ThenBy(c => c.Id);

        var specProj = new Specification<Customer, string>();
        var orderProj = specProj.Query.OrderByDescending(c => c.Name, condition: true);
        specProj.Query.Include(c => c.Address, condition: false);
        orderProj.ThenByDescending(c => c.Id);

        spec.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenBy);
        specProj.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderByDescending, OrderTypeEnum.ThenByDescending);
    }

    [Fact]
    public void DoesNotDropActiveThenInclude_WhenDiscardedCacheCallIntervenes()
    {
        var spec1 = new Specification<Customer>();
        var inc1 = spec1.Query.Include(c => c.Address, condition: true);
        spec1.Query.EnableCache("key", condition: false);
        inc1.ThenInclude(a => a.Contact);

        var spec2 = new Specification<Customer>();
        var inc2 = spec2.Query.Include(c => c.Address, condition: true);
        spec2.Query.WithCacheKey("key", condition: false);
        inc2.ThenInclude(a => a.Contact);

        spec1.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
        spec2.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
    }

    [Fact]
    public void DoesNotDropActiveThenBy_WhenDiscardedCacheCallIntervenes()
    {
        var spec1 = new Specification<Customer>();
        var ord1 = spec1.Query.OrderBy(c => c.Name, condition: true);
        spec1.Query.EnableCache("key", condition: false);
        ord1.ThenBy(c => c.Id);

        var spec2 = new Specification<Customer>();
        var ord2 = spec2.Query.OrderBy(c => c.Name, condition: true);
        spec2.Query.WithCacheKey("key", condition: false);
        ord2.ThenByDescending(c => c.Id);

        spec1.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenBy);
        spec2.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenByDescending);
    }

    [Fact]
    public void DoesNotDropActiveThenInclude_AcrossTwoSpecifications()
    {
        var spec1 = new Specification<Customer>();
        var spec2 = new Specification<Customer>();
        var inc1 = spec1.Query.Include(c => c.Address, condition: true);
        spec2.Query.Include(c => c.Address, condition: false);
        inc1.ThenInclude(a => a.Contact);

        spec1.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
    }

    [Fact]
    public async Task DoesNotDropActiveThenInclude_AcrossDirtyThreadHop()
    {
        var spec = new Specification<Customer>();
        var inc = spec.Query.Include(c => c.Address, condition: true);
        await RunOnFreshThreadAsync(() =>
        {
            var other = new Specification<Customer>();
            other.Query.Include(c => c.Address, condition: false);
            inc.ThenInclude(a => a.Contact);
        });

        spec.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
    }

    [Fact]
    public void IsolatesFluentHandoffAndBranchingHandles()
    {
        var s1 = new Specification<Customer>();
        var inc = s1.Query.Include(c => c.Address, condition: false);
        var ord = inc.OrderBy(c => c.Id, condition: true);
        ord.ThenBy(c => c.Name);
        inc.ThenInclude(a => a.Contact);

        var s2 = new Specification<Customer>();
        var inc2 = s2.Query.Include(c => c.Address, condition: true);
        var deadInc = inc2.ThenInclude(a => a.Contact, condition: false);
        deadInc.ThenInclude(ct => ct.Phone);
        inc2.ThenInclude(a => a.Contacts);

        var s3 = new Specification<Customer>();
        var ord2 = s3.Query.OrderBy(c => c.Name, condition: true);
        var deadOrd = ord2.ThenBy(c => c.Id, condition: false);
        deadOrd.ThenBy(c => c.Name);
        ord2.ThenByDescending(c => c.Id);

        s1.IncludeExpressions.Should().BeEmpty();
        s1.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenBy);
        s2.IncludeExpressions.Select(x => x.Type).Should().Equal(IncludeTypeEnum.Include, IncludeTypeEnum.ThenInclude);
        s3.OrderExpressions.Select(x => x.OrderType).Should().Equal(OrderTypeEnum.OrderBy, OrderTypeEnum.ThenByDescending);
    }

    private static Task RunOnFreshThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
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

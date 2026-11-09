using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike;

// Specs as they're written today, for the benchmark's "today" rows.

public sealed class CustomerByIdSpecToday : Specification<Customer>, ISingleResultSpecification<Customer>
{
    public CustomerByIdSpecToday(int id)
    {
        Query.Where(c => c.Id == id).Include(c => c.Orders).AsNoTracking();
    }
}

public sealed class CustomerSearchSpecToday : Specification<Customer>
{
    public CustomerSearchSpecToday(int regionId, string? namePrefix, CustomerSort sort)
    {
        Query.Where(c => c.RegionId == regionId);
        if (namePrefix is not null) Query.Where(c => c.Name.StartsWith(namePrefix));
        if (sort == CustomerSort.Newest) Query.OrderByDescending(c => c.CreatedOn);
        else Query.OrderBy(c => c.Name);
        Query.AsNoTracking();
    }
}

// Today's search spec with every lambda passed through the scan the core builder would add.
public sealed class CustomerSearchSpecTodayScanned : Specification<Customer>
{
    public CustomerSearchSpecTodayScanned(int regionId, string? namePrefix, CustomerSort sort)
    {
        Query.Where(QueryArgs.StripIfNeeded<Customer>(c => c.RegionId == regionId));
        if (namePrefix is not null) Query.Where(QueryArgs.StripIfNeeded<Customer>(c => c.Name.StartsWith(namePrefix)));
        if (sort == CustomerSort.Newest) Query.OrderByDescending(c => c.CreatedOn);
        else Query.OrderBy(c => c.Name);
        Query.AsNoTracking();
    }
}

// The same specs opted in to compiling: only the argument types change.

public sealed class CustomerByIdSpec : Specification<Customer>, ISingleResultSpecification<Customer>
{
    public CustomerByIdSpec(QueryArg<int> id)
    {
        Query.Where(c => c.Id == id).Include(c => c.Orders).AsNoTracking();
    }
}

// Two ways to avoid the conversion on the normal path, to compare their cost.

public sealed class CustomerByIdSpecValue : Specification<Customer>, ISingleResultSpecification<Customer>
{
    public CustomerByIdSpecValue(QueryArg<int> id)
    {
        Query.Where(c => c.Id == id.Value).Include(c => c.Orders).AsNoTracking();
    }
}

public sealed class CustomerByIdSpecStripped : Specification<Customer>, ISingleResultSpecification<Customer>
{
    public CustomerByIdSpecStripped(QueryArg<int> id)
    {
        Query.Where(QueryArgs.StripIfNeeded<Customer>(c => c.Id == id)).Include(c => c.Orders).AsNoTracking();
    }
}

public sealed class CustomerSearchSpecStripped : Specification<Customer>
{
    public CustomerSearchSpecStripped(QueryArg<int> regionId, QueryArg<string?> namePrefix, QueryArg<CustomerSort> sort)
    {
        Query.Where(QueryArgs.StripIfNeeded<Customer>(c => c.RegionId == regionId));
        if (namePrefix.HasValue) Query.Where(QueryArgs.StripIfNeeded<Customer>(c => c.Name.StartsWith(namePrefix!)));
        if (sort == CustomerSort.Newest) Query.OrderByDescending(c => c.CreatedOn);
        else Query.OrderBy(c => c.Name);
        Query.AsNoTracking();
    }
}

public sealed class CustomerSearchSpec : Specification<Customer>
{
    public CustomerSearchSpec(QueryArg<int> regionId, QueryArg<string?> namePrefix, QueryArg<CustomerSort> sort)
    {
        Query.Where(c => c.RegionId == regionId);
        if (namePrefix.HasValue) Query.Where(c => c.Name.StartsWith(namePrefix.Value!));
        if (sort == CustomerSort.Newest) Query.OrderByDescending(c => c.CreatedOn);
        else Query.OrderBy(c => c.Name);
        Query.AsNoTracking();
    }
}

public sealed class CustomerNamesSpec : Specification<Customer, CustomerName>
{
    public CustomerNamesSpec(QueryArg<int[]> ids)
    {
        Query.Where(c => ids.Value.Contains(c.Id)).OrderBy(c => c.Id);
        Query.Select(c => new CustomerName(c.Id, c.Name));
    }
}

public sealed class CustomerDetailsSpec : Specification<Customer>, ISingleResultSpecification<Customer>
{
    public CustomerDetailsSpec(QueryArg<int> id, QueryArg<bool> readOnly)
    {
        Query.Where(c => c.Id == id).Include(c => c.Orders).ThenInclude(o => o.Lines);
        if (readOnly) Query.AsNoTracking();
    }
}

public sealed class CustomerByNicknameSpec : Specification<Customer>
{
    public CustomerByNicknameSpec(QueryArg<string?> nickname)
    {
        Query.Where(c => c.Nickname == nickname).OrderBy(c => c.Id).AsNoTracking();
    }
}

public sealed class CustomerFixedSearchSpec : Specification<Customer>
{
    public CustomerFixedSearchSpec(QueryArg<int> regionId)
    {
        Query.Where(c => c.RegionId == regionId).Search(c => c.Name, "Smith%").OrderBy(c => c.Id).AsNoTracking();
    }
}

// Specs the library must refuse.

public sealed class CustomersAboveIdSpec : Specification<Customer>
{
    public CustomersAboveIdSpec(QueryArg<int> minimum)
    {
        if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum));
        Query.Where(c => c.Id > minimum);
    }
}

public sealed class CustomersForTenantSpec : Specification<Customer>
{
    public CustomersForTenantSpec(QueryArg<int> regionId)
    {
        Query.Where(c => c.RegionId == regionId && c.TenantId == TenantContext.CurrentTenantId);
    }
}

public sealed class CustomersForTenantLocalSpec : Specification<Customer>
{
    public CustomersForTenantLocalSpec(QueryArg<int> regionId)
    {
        var tenantId = TenantContext.CurrentTenantId;
        Query.Where(c => c.RegionId == regionId && c.TenantId == tenantId);
    }
}

public sealed class NextCustomerSpec : Specification<Customer>
{
    public NextCustomerSpec(QueryArg<int> id)
    {
        Query.Where(c => c.Id == id + 1);
    }
}

public sealed class CustomersWithPermissionSpec : Specification<Customer>
{
    public CustomersWithPermissionSpec(QueryArg<Permissions> permissions)
    {
        if (permissions == Permissions.Read) Query.Where(c => c.IsActive);
    }
}

public sealed class PostProcessedSpec : Specification<Customer>
{
    public PostProcessedSpec(QueryArg<int> regionId)
    {
        Query.Where(c => c.RegionId == regionId);
        Query.PostProcessingAction(rows => rows.Reverse());
    }
}

public sealed class TwoRegionSpec : Specification<Customer>
{
    public TwoRegionSpec(QueryArg<int> first, QueryArg<int> second)
    {
        Query.Where(c => c.RegionId == first || c.RegionId == second);
    }
}

// The library can't detect this one: a branch on outside state.
public sealed class ActiveCustomersSpec : Specification<Customer>
{
    public ActiveCustomersSpec(QueryArg<int> regionId)
    {
        Query.Where(c => c.RegionId == regionId).OrderBy(c => c.Id).AsNoTracking();
        if (FeatureFlags.OnlyActive) Query.Where(c => c.IsActive);
    }
}

public static class Queries
{
    public static readonly CompiledSpecification<Customer, int> ById =
        CompiledSpecification.Create<Customer, int>(id => new CustomerByIdSpec(id));

    public static readonly CompiledSpecification<Customer, int, string?, CustomerSort> Search =
        CompiledSpecification.Create<Customer, int, string?, CustomerSort>(
            (regionId, namePrefix, sort) => new CustomerSearchSpec(regionId, namePrefix, sort));

    public static readonly CompiledSpecification<CustomerName, int[]> Names =
        CompiledSpecification.Create<Customer, CustomerName, int[]>(ids => new CustomerNamesSpec(ids));

    public static readonly CompiledSpecification<Customer, int, bool> Details =
        CompiledSpecification.Create<Customer, int, bool>((id, readOnly) => new CustomerDetailsSpec(id, readOnly));

    public static readonly CompiledSpecification<Customer, string?> ByNickname =
        CompiledSpecification.Create<Customer, string?>(nickname => new CustomerByNicknameSpec(nickname));

    public static readonly CompiledSpecification<Customer, int> ByIdValue =
        CompiledSpecification.Create<Customer, int>(id => new CustomerByIdSpecValue(id));

    public static readonly CompiledSpecification<Customer, int> ByIdStripped =
        CompiledSpecification.Create<Customer, int>(id => new CustomerByIdSpecStripped(id));

    public static readonly CompiledSpecification<Customer, int, string?, CustomerSort> SearchStripped =
        CompiledSpecification.Create<Customer, int, string?, CustomerSort>(
            (regionId, namePrefix, sort) => new CustomerSearchSpecStripped(regionId, namePrefix, sort));

    public static readonly CompiledSpecification<Customer, int> FixedSearch =
        CompiledSpecification.Create<Customer, int>(regionId => new CustomerFixedSearchSpec(regionId));
}

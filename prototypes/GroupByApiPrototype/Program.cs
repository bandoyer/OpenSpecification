using GroupByApiPrototype;
using GroupByApiPrototype.CandidateA;
using GroupByApiPrototype.CandidateB;
using GroupByApiPrototype.CandidateC;
using GroupByApiPrototype.CandidateD;
using OpenSpecification;

Console.WriteLine("=== OpenSpecification #91: GroupBy() Builder API Prototype ===\n");

var repo = new PrototypeInMemoryRepository<Store>(SampleData.Stores);

async Task RunCase<TResult>(string label, ISpecification<Store, TResult> spec)
{
    var plan = PrototypePlanStore.Get(spec);
    var rows = await repo.ListAsync(spec);
    Console.WriteLine($"[{label}]");
    Console.WriteLine($"  Plan:   {plan?.Describe()}");
    Console.WriteLine($"  KeyType: {plan?.KeyType.Name}");
    Console.WriteLine($"  Result ({rows.Count}): {string.Join(" | ", rows.Select(FormatResult))}");
}

static string FormatResult<T>(T item) => item switch
{
    CitySalesDto d => $"{(d.CompanyId > 0 ? $"C{d.CompanyId}/" : "")}{d.City}(n={d.StoreCount}, sum={d.TotalAmount}, max={d.MaxAmount})",
    _ => item?.ToString() ?? "<null>"
};

Console.WriteLine("--- Scenario 1: Single-key grouped projection ---");
await RunCase("A1   Fluent Staged", new A1_SingleKeySpec());
await RunCase("A1b  Fluent + 2-arg shorthand", new A1b_ShorthandSingleKeySpec());
await RunCase("B1   Callback Scoped", new B1_SingleKeySpec());
await RunCase("C1   Single-Call TResult-Stage", new C1_SingleKeySpec());
await RunCase("D1   GroupedSpecification Subclass", new D1_SingleKeySpec());

Console.WriteLine("\n--- Scenario 2: Composite key + Having + OrderByDesc/ThenBy + Skip/Take ---");
await RunCase("A2   (highVol=true, sortRev=true)",  new A2_CompositeKeyHavingOrderPageSpec(true, true));
await RunCase("A2   (highVol=true, sortRev=false)", new A2_CompositeKeyHavingOrderPageSpec(true, false));
await RunCase("B2   (highVol=true, sortRev=true)",  new B2_CompositeKeyHavingOrderPageSpec(true, true));
await RunCase("B2   (highVol=true, sortRev=false)", new B2_CompositeKeyHavingOrderPageSpec(true, false));
await RunCase("C2   (highVol=true, sortRev=true)",  new C2_CompositeKeyHavingOrderPageSpec(true, true));
await RunCase("C2   (highVol=true, sortRev=false)", new C2_CompositeKeyHavingOrderPageSpec(true, false));
await RunCase("D2   (highVol=true, sortRev=true)",  new D2_CompositeKeyHavingOrderPageSpec(true, true));

Console.WriteLine("\n--- Scenario 5: Unprojected aggregates (Specification<Store, string>) ---");
await RunCase("A5   Fluent Staged", new A5_UnprojectedAggregateScalarSpec());
await RunCase("B5   Callback Scoped", new B5_UnprojectedAggregateScalarSpec());
Console.WriteLine("[C5   Single-Call TResult-Stage]\n  NOT EXPRESSIBLE: TResult is `string`; g.Count() / g.Sum() unavailable after GroupBy(key, g => g.Key).");
await RunCase("D5   GroupedSpecification Subclass", new D5_UnprojectedAggregateScalarSpec());

Console.WriteLine("\n--- Scenario 6: PagedAsync (group count + page slice via ISpecification<T, TResult>) ---");
var pagedSpec = new A2_CompositeKeyHavingOrderPageSpec(highVolumeOnly: true, sortByRevenue: true);
var (totalGroups, page1) = await repo.PagedAsync(pagedSpec, page: 1, pageSize: 2);
Console.WriteLine($"[A2 Paged page=1, size=2] TotalGroups={totalGroups}, PageItems={string.Join(" | ", page1.Select(FormatResult))}");

Console.WriteLine("\n--- Scenario 7: Split-statement dynamic building (anonymous composite key) ---");
await RunCase("A7   Fluent Staged (var group = ...)", new A7_SplitStatementDynamicSpec(minStores: 2, sortByTotal: true));
await RunCase("B7   Callback Scoped (block lambda)",  new B7_SplitStatementDynamicSpec(minStores: 2, sortByTotal: true));
await RunCase("C7   TResult-Stage (typed builder)",   new C7_SplitStatementDynamicSpec(minStores: 2, sortByTotal: true));

Console.WriteLine("\n--- Runtime error checks ---");
try
{
    var missingSelect = new MissingSelectRuntimeSpec();
    await repo.ListAsync(missingSelect);
}
catch (Exception ex)
{
    Console.WriteLine($"  Candidate A missing Select -> {ex.GetType().Name}: {ex.Message}");
}

try
{
    var mixedSpec = new MixedSelectAndGroupByRuntimeSpec();
    await repo.ListAsync(mixedSpec);
}
catch (Exception ex)
{
    Console.WriteLine($"  Mixing Select + GroupBy    -> {ex.GetType().Name}: {ex.Message}");
}

try
{
    _ = new DoubleGroupByRuntimeSpec();
}
catch (Exception ex)
{
    Console.WriteLine($"  Calling GroupBy twice      -> {ex.GetType().Name}: {ex.Message}");
}

sealed class MissingSelectRuntimeSpec : Specification<Store, CitySalesDto>
{
    public MissingSelectRuntimeSpec()
    {
        Query.Where(s => s.IsActive).GroupBy(s => s.City).Having(g => g.Count() >= 2);
    }
}

sealed class MixedSelectAndGroupByRuntimeSpec : Specification<Store, CitySalesDto>
{
    public MixedSelectAndGroupByRuntimeSpec()
    {
        Query.Select(s => new CitySalesDto { City = s.City });
        Query.GroupBy(s => s.City).Select(g => new CitySalesDto { City = g.Key });
    }
}

sealed class DoubleGroupByRuntimeSpec : Specification<Store, CitySalesDto>
{
    public DoubleGroupByRuntimeSpec()
    {
        Query.GroupBy(s => s.City).Select(g => new CitySalesDto { City = g.Key });
        Query.GroupBy(s => s.CompanyId).Select(g => new CitySalesDto { CompanyId = g.Key });
    }
}

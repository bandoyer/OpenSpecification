using OpenSpecification;

namespace GroupByApiPrototype;

public sealed class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class Store
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
}

public sealed record CitySalesDto
{
    public int CompanyId { get; init; }
    public string City { get; init; } = string.Empty;
    public int StoreCount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal MaxAmount { get; init; }
}

/// <summary>
/// Named composite key required by Candidate D (GroupedSpecification&lt;T, TKey, TResult&gt;).
/// Must have parameterless ctor + init properties for EF6 compatibility (#89) AND value equality for In-Memory.
/// </summary>
public sealed record CompanyCityKey
{
    public int CompanyId { get; init; }
    public string City { get; init; } = string.Empty;
}

public static class SampleData
{
    public static readonly List<Store> Stores =
    [
        new() { Id = 1, CompanyId = 1, Company = new() { Id = 1, Name = "Acme" }, Name = "A1", City = "Austin", Amount = 100m, IsActive = true },
        new() { Id = 2, CompanyId = 1, Company = new() { Id = 1, Name = "Acme" }, Name = "A2", City = "Austin", Amount = 250m, IsActive = true },
        new() { Id = 3, CompanyId = 1, Company = new() { Id = 1, Name = "Acme" }, Name = "B1", City = "Boston", Amount = 400m, IsActive = true },
        new() { Id = 4, CompanyId = 1, Company = new() { Id = 1, Name = "Acme" }, Name = "B2", City = "Boston", Amount = 50m,  IsActive = false },
        new() { Id = 5, CompanyId = 2, Company = new() { Id = 2, Name = "Beta" }, Name = "A3", City = "Austin", Amount = 300m, IsActive = true },
        new() { Id = 6, CompanyId = 2, Company = new() { Id = 2, Name = "Beta" }, Name = "A4", City = "Austin", Amount = 120m, IsActive = true },
        new() { Id = 7, CompanyId = 2, Company = new() { Id = 2, Name = "Beta" }, Name = "D1", City = "Denver", Amount = 500m, IsActive = true },
        new() { Id = 8, CompanyId = 2, Company = new() { Id = 2, Name = "Beta" }, Name = "D2", City = "Denver", Amount = 100m, IsActive = true },
    ];
}

/// <summary>
/// Proves unchanged IReadRepositoryBase&lt;T&gt; compatibility: accepts standard ISpecification&lt;T, TResult&gt;
/// with ZERO new repository overloads and ZERO TKey leakage.
/// </summary>
public sealed class PrototypeInMemoryRepository<T>(IEnumerable<T> source) where T : class
{
    private readonly IEnumerable<T> _source = source;

    // Exact same signature shape as IReadRepositoryBase<T>.ListAsync<TResult>(ISpecification<T, TResult>)
    public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification)
    {
        var results = PrototypeEvaluator.Evaluate(_source, specification).ToList();
        return Task.FromResult(results);
    }

    // Simulates PagedListAsync<TResult> counting groups vs returning a page
    public Task<(int TotalGroups, List<TResult> Items)> PagedAsync<TResult>(
        ISpecification<T, TResult> specification,
        int page,
        int pageSize)
    {
        var (count, items) = PrototypeEvaluator.EvaluatePaged(_source, specification, page, pageSize);
        return Task.FromResult((count, items));
    }
}

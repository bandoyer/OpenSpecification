using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OpenSpecification.Spike;

public class Customer
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public DateTime CreatedOn { get; set; }
    public bool IsActive { get; set; }
    public int TenantId { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public long TotalCents { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int Quantity { get; set; }
}

public record CustomerName(int Id, string Name);

public enum CustomerSort { Name, Newest }

[Flags]
public enum Permissions { None = 0, Read = 1, Write = 2 }

public static class TenantContext
{
    public static int CurrentTenantId { get; set; } = 1;
}

public static class FeatureFlags
{
    public static bool OnlyActive { get; set; }
}

public class SpikeContext(DbContextOptions<SpikeContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
}

/// <summary>Maps the same tables with its own model, to check that a handle keeps one delegate per model.</summary>
public class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().ToTable("Customers");
        modelBuilder.Entity<Order>().ToTable("Order");
        modelBuilder.Entity<OrderLine>().ToTable("OrderLine");
    }
}

public static class Db
{
    private const string ConnectionString = "Data Source=openspec-spike;Mode=Memory;Cache=Shared";
    private static SqliteConnection? _keepAlive;

    public static DbContextOptions<SpikeContext> Options { get; } =
        new DbContextOptionsBuilder<SpikeContext>().UseSqlite(ConnectionString).Options;

    private static readonly DbContextOptions<OtherContext> _otherOptions =
        new DbContextOptionsBuilder<OtherContext>().UseSqlite(ConnectionString).Options;

    public static SpikeContext NewContext() => new(Options);
    public static OtherContext NewOtherContext() => new(_otherOptions);

    public static void Init()
    {
        if (_keepAlive is not null) return;
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using var context = NewContext();
        context.Database.EnsureCreated();

        string[] surnames = ["Smith", "Jones", "Brown", "Taylor", "Wilson", "Davies", "Evans"];
        var start = new DateTime(2024, 1, 1);
        for (var i = 1; i <= 1000; i++)
        {
            var customer = new Customer
            {
                Id = i,
                RegionId = i % 10,
                Name = $"{surnames[i % 7]} {i:D4}",
                Nickname = i % 5 == 0 ? null : $"nick{i % 3}",
                CreatedOn = start.AddDays(i),
                IsActive = i % 6 != 0,
                TenantId = i % 2,
            };

            for (var j = 0; j < i % 4; j++)
            {
                var order = new Order { TotalCents = i * 100 + j };
                for (var k = 0; k <= j % 2; k++) order.Lines.Add(new OrderLine { Quantity = k + 1 });
                customer.Orders.Add(order);
            }

            context.Customers.Add(customer);
        }

        context.SaveChanges();
    }
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartReport.Data.Entities;

namespace SmartReport.Data;

public sealed class SmartReportDbContext(DbContextOptions<SmartReportDbContext> options)
    : DbContext(options)
{
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Compensation> Compensation => Set<Compensation>();
    public DbSet<HeadcountMonthly> HeadcountMonthly => Set<HeadcountMonthly>();
    public DbSet<AttritionEvent> AttritionEvents => Set<AttritionEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Entities.Program> Programs => Set<Entities.Program>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The catalog's SQL expressions are written against snake_case physical names, so the
        // mapping is enforced here rather than left to per-property attributes that could drift.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        modelBuilder.Entity<Region>(b => b.Property(r => r.Name).HasMaxLength(60).IsRequired());
        modelBuilder.Entity<Department>(b => b.Property(d => d.Name).HasMaxLength(80).IsRequired());

        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(120).IsRequired();
            b.Property(p => p.Category).HasMaxLength(60).IsRequired();
            b.Property(p => p.UnitPrice).HasPrecision(12, 2);
        });

        modelBuilder.Entity<Employee>(b =>
        {
            b.Property(e => e.FullName).HasMaxLength(120).IsRequired();
            b.Property(e => e.Email).HasMaxLength(160).IsRequired();
            b.Property(e => e.Role).HasMaxLength(40).IsRequired();
            b.Property(e => e.PrincipalKey).HasMaxLength(40);
            b.HasIndex(e => e.PrincipalKey).IsUnique();
            b.HasIndex(e => e.RegionId);
        });

        modelBuilder.Entity<Compensation>(b =>
        {
            b.Property(c => c.AnnualSalary).HasPrecision(12, 2);
            b.HasIndex(c => c.EmployeeId);
            b.HasIndex(c => c.RegionId);
        });

        modelBuilder.Entity<HeadcountMonthly>(b => b.HasIndex(h => new { h.RegionId, h.Month }));

        modelBuilder.Entity<AttritionEvent>(b =>
        {
            b.Property(a => a.Reason).HasMaxLength(60).IsRequired();
            b.Property(a => a.ReplacementCost).HasPrecision(12, 2);
            b.HasIndex(a => new { a.RegionId, a.EventDate });
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(c => c.RegionId);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.Property(o => o.Status).HasMaxLength(30).IsRequired();
            b.HasIndex(o => new { o.RegionId, o.OrderedAt });
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.Property(i => i.UnitPrice).HasPrecision(12, 2);
            b.HasIndex(i => i.OrderId);
            b.HasIndex(i => i.RegionId);
        });

        modelBuilder.Entity<Entities.Program>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(120).IsRequired();
            b.Property(p => p.BudgetAmount).HasPrecision(12, 2);
            b.Property(p => p.SpendAmount).HasPrecision(12, 2);
        });

        modelBuilder.Entity<SavedReport>(b =>
        {
            b.Property(r => r.OwnerKey).HasMaxLength(40).IsRequired();
            b.Property(r => r.Title).HasMaxLength(160).IsRequired();
            b.Property(r => r.Question).HasMaxLength(500).IsRequired();
            b.Property(r => r.SpecJson).IsRequired();
            b.HasIndex(r => r.OwnerKey);
        });
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && !char.IsUpper(name[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                sb.Append(name[i]);
            }
        }
        return sb.ToString();
    }
}

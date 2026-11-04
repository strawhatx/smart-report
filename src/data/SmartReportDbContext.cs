using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartReport.Data.Entities;

namespace SmartReport.Data;

public sealed class SmartReportDbContext(DbContextOptions<SmartReportDbContext> options)
    : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientUser> ClientUsers => Set<ClientUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<Fee> Fees => Set<Fee>();
    public DbSet<CarrierContractRate> CarrierContractRates => Set<CarrierContractRate>();
    public DbSet<BenchmarkMetric> BenchmarkMetrics => Set<BenchmarkMetric>();
    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The catalog's SQL expressions are written against snake_case physical names, so the
        // mapping is enforced here rather than left to attributes that could drift out of sync.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        modelBuilder.Entity<Client>(b =>
        {
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Name).HasMaxLength(120).IsRequired();
            b.Property(c => c.Slug).HasMaxLength(60).IsRequired();
            b.HasIndex(c => c.Slug).IsUnique();
        });

        modelBuilder.Entity<ClientUser>(b =>
        {
            b.Property(u => u.Id).ValueGeneratedNever();
            b.Property(u => u.FullName).HasMaxLength(120).IsRequired();
            b.Property(u => u.Email).HasMaxLength(160).IsRequired();
            b.Property(u => u.Role).HasMaxLength(40).IsRequired();
            b.Property(u => u.JobTitle).HasMaxLength(80).IsRequired();
            b.Property(u => u.PrincipalKey).HasMaxLength(40);
            b.HasIndex(u => u.PrincipalKey).IsUnique();
            b.HasIndex(u => u.ClientId);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Sku).HasMaxLength(40).IsRequired();
            b.Property(p => p.Name).HasMaxLength(140).IsRequired();
            b.HasIndex(p => p.ClientId);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Channel).HasMaxLength(40).IsRequired();
            b.Property(o => o.Status).HasMaxLength(30).IsRequired();
            b.HasIndex(o => new { o.ClientId, o.OrderedAt });
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.Property(i => i.Id).ValueGeneratedNever();
            b.HasIndex(i => new { i.ClientId, i.OrderId });
        });

        modelBuilder.Entity<Shipment>(b =>
        {
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Carrier).HasMaxLength(40).IsRequired();
            b.Property(s => s.Service).HasMaxLength(40).IsRequired();
            b.Property(s => s.BilledAmount).HasPrecision(10, 2);
            b.HasIndex(s => new { s.ClientId, s.ShippedAt });
        });

        modelBuilder.Entity<Return>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Reason).HasMaxLength(60).IsRequired();
            b.Property(r => r.RefundAmount).HasPrecision(10, 2);
            b.HasIndex(r => new { r.ClientId, r.ReceivedAt });
        });

        modelBuilder.Entity<Fee>(b =>
        {
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.FeeType).HasMaxLength(60).IsRequired();
            b.Property(f => f.Amount).HasPrecision(12, 2);
            b.HasIndex(f => new { f.ClientId, f.PeriodMonth });
        });

        modelBuilder.Entity<CarrierContractRate>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Carrier).HasMaxLength(40).IsRequired();
            b.Property(r => r.Service).HasMaxLength(40).IsRequired();
            b.Property(r => r.CostPerLabel).HasPrecision(8, 3);
        });

        modelBuilder.Entity<BenchmarkMetric>(b =>
        {
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.MetricKey).HasMaxLength(60).IsRequired();
            b.Property(m => m.DimensionKey).HasMaxLength(40).IsRequired();
            b.Property(m => m.DimensionValue).HasMaxLength(60).IsRequired();
            b.Property(m => m.P25).HasPrecision(10, 3);
            b.Property(m => m.P50).HasPrecision(10, 3);
            b.Property(m => m.P75).HasPrecision(10, 3);
            b.HasIndex(m => new { m.MetricKey, m.PeriodMonth });
        });

        modelBuilder.Entity<ReportTemplate>(b =>
        {
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Title).HasMaxLength(120).IsRequired();
            b.Property(t => t.Question).HasMaxLength(400).IsRequired();
            b.Property(t => t.Category).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<SavedReport>(b =>
        {
            b.Property(r => r.OwnerKey).HasMaxLength(40).IsRequired();
            b.Property(r => r.Title).HasMaxLength(160).IsRequired();
            b.Property(r => r.Question).HasMaxLength(500).IsRequired();
            b.HasIndex(r => new { r.ClientId, r.OwnerKey });
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

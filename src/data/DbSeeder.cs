using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SmartReport.Data.Entities;

namespace SmartReport.Data;

/// <summary>
/// Fills an empty database with Northpoint and its clients.
///
/// <para>
/// Runs on the <b>owner</b> connection — one of the two places that is correct (D4). The
/// application role has SELECT only, so it could not write this even if asked to.
/// </para>
/// <para>
/// Reference tables go through EF. The ~430,000 operational rows go through <c>COPY</c>: Trailhead
/// has twenty-two months of real orders so that every figure in the report is a genuine
/// aggregation rather than a number typed into the UI, and EF's change tracker is the wrong tool
/// for that volume.
/// </para>
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(SmartReportDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Clients.AnyAsync(cancellationToken))
        {
            return;
        }

        var random = new Random(SeedPlan.Seed);

        var clients = BuildClients();
        db.Clients.AddRange(clients);
        db.ClientUsers.AddRange(BuildUsers());
        db.ReportTemplates.AddRange(BuildTemplates());
        db.CarrierContractRates.AddRange(BuildCarrierRates());
        db.BenchmarkMetrics.AddRange(BuildBenchmarks());

        var trailhead = clients.Single(c => c.Slug == SeedPlan.TrailheadSlug);
        var bluefin = clients.Single(c => c.Slug == SeedPlan.BluefinSlug);

        var products = BuildProducts(trailhead.Id, bluefin.Id);
        db.Products.AddRange(products);
        db.Fees.AddRange(BuildFees(trailhead.Id, bluefin.Id));

        await db.SaveChangesAsync(cancellationToken);

        // Operational rows: Trailhead in full, Bluefin small but real so the isolation tests have
        // something that would actually show up if a policy were wrong.
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The seeder needs the owner connection string.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var writer = new OperationalWriter(connection, random);
        await writer.WriteClientAsync(trailhead.Id, SeedPlan.Volume2025, SeedPlan.Volume2026,
            products.Where(p => p.ClientId == trailhead.Id).Select(p => p.Id).ToArray(),
            isHeadline: true, cancellationToken);

        await writer.WriteClientAsync(bluefin.Id, [420, 390, 450, 470, 500, 510, 530, 560, 600, 640],
            [560, 540, 600, 630, 660, 650, 700, 740, 790, 850],
            products.Where(p => p.ClientId == bluefin.Id).Select(p => p.Id).ToArray(),
            isHeadline: false, cancellationToken);
    }

    private static List<Client> BuildClients()
    {
        var clients = new List<Client>
        {
            new() { Id = 1, Name = "Trailhead Coffee Co.", Slug = SeedPlan.TrailheadSlug, JoinedOn = new DateOnly(2024, 6, 3) },
        };

        var id = 2;
        foreach (var name in SeedPlan.OtherClientNames)
        {
            var slug = name.ToLowerInvariant().Replace(" & ", "-").Replace(' ', '-').Replace(".", "");
            clients.Add(new Client { Id = id++, Name = name, Slug = slug, JoinedOn = new DateOnly(2024, 1, 1).AddDays(id * 11) });
        }

        return clients;
    }

    private static IEnumerable<ClientUser> BuildUsers() =>
    [
        // The two the demo signs in as. Same company, different authority — which is the point.
        new()
        {
            Id = 1, ClientId = 1, FullName = "Dana Whitfield", Email = "dana@trailheadcoffee.example",
            Role = ClientRoles.Coordinator, JobTitle = "Warehouse Coordinator",
            PrincipalKey = DemoPrincipals.Coordinator, IsActive = true,
        },
        new()
        {
            Id = 2, ClientId = 1, FullName = "Priya Shah", Email = "priya@trailheadcoffee.example",
            Role = ClientRoles.AccountOwner, JobTitle = "Account Owner",
            PrincipalKey = DemoPrincipals.AccountOwner, IsActive = true,
        },
        // Bluefin has people too. Not signable-in: they exist so the isolation tests are testing
        // something real rather than an empty table.
        new()
        {
            Id = 3, ClientId = 2, FullName = "Sam Oyelaran", Email = "sam@bluefinapparel.example",
            Role = ClientRoles.AccountOwner, JobTitle = "Account Owner",
            PrincipalKey = null, IsActive = true,
        },
    ];

    private static IEnumerable<ReportTemplate> BuildTemplates() =>
    [
        new() { Id = 1, Title = "October shipping report", Category = "Shipping", SortOrder = 1,
            Question = "How did our orders ship in October? Show late shipments by carrier, orders by channel, and the trend." },
        new() { Id = 2, Title = "Late shipments by carrier", Category = "Shipping", SortOrder = 2,
            Question = "Show late shipments by carrier this month" },
        new() { Id = 3, Title = "On-time rate vs peers", Category = "Benchmarks", SortOrder = 3,
            Question = "How does our on-time rate compare to other Northpoint clients?" },
        new() { Id = 4, Title = "Orders by sales channel", Category = "Orders", SortOrder = 4,
            Question = "Break our orders down by sales channel" },
        new() { Id = 5, Title = "What is running out", Category = "Inventory", SortOrder = 5,
            Question = "Which products are low on stock?" },
        new() { Id = 6, Title = "Returns by reason", Category = "Returns", SortOrder = 6,
            Question = "Show returns by reason this quarter" },
        new() { Id = 7, Title = "Monthly fees", Category = "Billing", SortOrder = 7,
            Question = "Show our fees and invoice breakdown by month" },
        new() { Id = 8, Title = "Cost per shipment", Category = "Shipping", SortOrder = 8,
            Question = "What are we billed per shipment by carrier?" },
    ];

    private static IEnumerable<CarrierContractRate> BuildCarrierRates()
    {
        long id = 1;
        foreach (var (carrier, service, _, billed) in SeedPlan.CarrierMix)
        {
            // Northpoint's margin lives in the gap between this and what the client is billed.
            yield return new CarrierContractRate
            {
                Id = id++, Carrier = carrier, Service = service,
                CostPerLabel = Math.Round(billed * 0.62m, 3),
                EffectiveFrom = new DateOnly(2026, 1, 1),
            };
        }
    }

    private static IEnumerable<BenchmarkMetric> BuildBenchmarks()
    {
        long id = 1;
        // Both percentages, so the benchmark metrics can carry one unit. The figures below are
        // where the cohort lands in the final month; earlier months drift down from them.
        var metrics = new (string Key, decimal P25, decimal P50, decimal P75)[]
        {
            ("on_time_rate", 93.1m, 95.8m, 97.9m),
            ("return_rate", 4.4m, 3.3m, 2.1m),
        };

        for (var month = new DateOnly(2025, 1, 1); month <= SeedPlan.AsOf; month = month.AddMonths(1))
        {
            var cohort = month < SeedPlan.CohortGrewOn ? SeedPlan.CohortEarly2025 : SeedPlan.CohortNow;
            // Drift is measured back from the final month, so the headline month reads exactly
            // the figures above rather than those figures plus however long the series is.
            var monthsBack = ((SeedPlan.AsOf.Year - month.Year) * 12) + (SeedPlan.AsOf.Month - month.Month);
            var drift = monthsBack * -0.04m;

            foreach (var (key, p25, p50, p75) in metrics)
            {
                yield return new BenchmarkMetric
                {
                    Id = id++,
                    MetricKey = key,
                    PeriodMonth = new DateOnly(month.Year, month.Month, 1),
                    DimensionKey = "all",
                    DimensionValue = "All clients",
                    ClientCount = cohort,
                    P25 = p25 + drift,
                    P50 = p50 + drift,
                    P75 = p75 + drift,
                };
            }
        }
    }

    private static List<Product> BuildProducts(int trailheadId, int bluefinId)
    {
        string[] trailhead =
        [
            "Ridgeline Espresso 1kg", "Ridgeline Espresso 250g", "Summit Filter 1kg", "Summit Filter 250g",
            "Switchback Decaf 250g", "Basecamp Blend 1kg", "Basecamp Blend 250g", "Treeline Single Origin",
            "Cold Brew Concentrate", "Trailhead Mug", "Pourover Kit", "Travel Press",
            "Paper Filters 100ct", "Descaling Tablets", "Gift Box — Explorer", "Gift Box — Summit",
            "Subscription — Monthly", "Subscription — Fortnightly", "Barista Apron", "Trailhead Tote",
        ];

        string[] bluefin =
        [
            "Harbor Jacket", "Harbor Jacket XL", "Deckhand Tee", "Deckhand Tee 2pk",
            "Current Shorts", "Tidal Hoodie", "Anchor Cap", "Breakwater Socks",
        ];

        var products = new List<Product>();
        long id = 1;

        for (var i = 0; i < trailhead.Length; i++)
        {
            // Exactly six sit at or below their reorder point — the "low-stock products" KPI.
            var low = i is 4 or 8 or 11 or 13 or 16 or 18;
            products.Add(new Product
            {
                Id = id++, ClientId = trailheadId,
                Sku = $"TRH-{1000 + i}", Name = trailhead[i],
                OnHand = low ? 18 + i : 240 + i * 37,
                ReorderPoint = low ? 40 + i : 60,
            });
        }

        for (var i = 0; i < bluefin.Length; i++)
        {
            products.Add(new Product
            {
                Id = id++, ClientId = bluefinId,
                Sku = $"BLU-{2000 + i}", Name = bluefin[i],
                OnHand = 120 + i * 24, ReorderPoint = 50,
            });
        }

        return products;
    }

    private static IEnumerable<Fee> BuildFees(int trailheadId, int bluefinId)
    {
        long id = 1;
        var rates = new Dictionary<string, decimal>
        {
            ["Storage"] = 1_840m, ["Pick & pack"] = 0m, ["Shipping"] = 0m,
            ["Returns handling"] = 0m, ["Account management"] = 450m,
        };

        foreach (var clientId in new[] { trailheadId, bluefinId })
        {
            var scale = clientId == trailheadId ? 1m : 0.11m;

            for (var month = new DateOnly(2025, 1, 1); month <= SeedPlan.AsOf; month = month.AddMonths(1))
            {
                var volume = clientId == trailheadId ? VolumeFor(month) : VolumeFor(month) * 0.11;

                foreach (var feeType in SeedPlan.FeeTypes)
                {
                    var amount = feeType switch
                    {
                        "Pick & pack" => (decimal)volume * 1.35m,
                        "Shipping" => (decimal)volume * 7.20m,
                        "Returns handling" => (decimal)volume * SeedPlan.ReturnRate.ToDecimal() * 3.10m,
                        _ => rates[feeType] * scale,
                    };

                    yield return new Fee
                    {
                        Id = id++, ClientId = clientId,
                        PeriodMonth = new DateOnly(month.Year, month.Month, 1),
                        FeeType = feeType,
                        Amount = Math.Round(amount, 2),
                    };
                }
            }
        }
    }

    internal static double VolumeFor(DateOnly month)
    {
        var table = month.Year == 2025 ? SeedPlan.Volume2025 : SeedPlan.Volume2026;
        return month.Month <= table.Length ? table[month.Month - 1] : table[^1];
    }

    private static decimal ToDecimal(this double value) => (decimal)value;
}

using Microsoft.EntityFrameworkCore;
using SmartReport.Core.Policy;
using SmartReport.Data.Entities;

namespace SmartReport.Data;

/// <summary>
/// Fills an empty database with the demo company.
///
/// <para>
/// Runs on the <b>owner</b> connection, which is one of the two places that is correct (D4) —
/// the application role has SELECT only, so it could not write this even if asked to.
/// </para>
/// <para>
/// Everything is generated from a fixed seed, so the numbers in the demo are the same on every
/// machine and a screenshot stays true.
/// </para>
/// </summary>
public static class DbSeeder
{
    private const int Seed = 20261103;

    /// <summary>The data ends here rather than at "now", so the demo reads the same next month.</summary>
    private static readonly DateOnly AsOf = new(2026, 10, 31);

    private static readonly string[] DepartmentNames =
        ["Engineering", "Sales", "Customer Success", "People Ops", "Finance", "Marketing"];

    private static readonly string[] RegionNames = ["North", "South", "East"];

    private static readonly string[] AttritionReasons =
        ["Resigned", "Better offer", "Relocation", "Retirement", "Performance", "Redundancy"];

    private static readonly string[] OrderStatuses = ["fulfilled", "fulfilled", "fulfilled", "pending", "cancelled"];

    private static readonly (string Name, string Category, decimal Price)[] ProductCatalog =
    [
        ("Atlas Starter", "Platform", 1_200m), ("Atlas Pro", "Platform", 4_800m),
        ("Atlas Enterprise", "Platform", 18_000m), ("Beacon Analytics", "Analytics", 3_600m),
        ("Beacon Reporting", "Analytics", 2_400m), ("Beacon Forecast", "Analytics", 5_200m),
        ("Relay Connect", "Integration", 900m), ("Relay Sync", "Integration", 1_800m),
        ("Relay Gateway", "Integration", 6_400m), ("Sentry Audit", "Security", 2_900m),
        ("Sentry Vault", "Security", 7_300m), ("Sentry Watch", "Security", 4_100m),
        ("Onboarding Package", "Services", 2_000m), ("Migration Package", "Services", 8_500m),
        ("Support Standard", "Services", 1_100m), ("Support Premium", "Services", 3_300m),
    ];

    public static async Task SeedAsync(SmartReportDbContext db, CancellationToken cancellationToken = default)
    {
        // Each block checks for itself rather than one early return for the whole method, so a
        // database seeded before a block existed still picks it up on the next start.
        if (!await db.SavedReports.AnyAsync(cancellationToken))
        {
            db.SavedReports.AddRange(BuildSavedReports());
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.Regions.AnyAsync(cancellationToken))
        {
            return;
        }

        var random = new Random(Seed);

        var regions = RegionNames.Select(n => new Region { Name = n }).ToArray();
        db.Regions.AddRange(regions);

        var departments = DepartmentNames.Select(n => new Department { Name = n }).ToArray();
        db.Departments.AddRange(departments);

        var products = ProductCatalog
            .Select(p => new Product { Name = p.Name, Category = p.Category, UnitPrice = p.Price })
            .ToArray();
        db.Products.AddRange(products);

        await db.SaveChangesAsync(cancellationToken);

        var employees = BuildEmployees(random, regions, departments);
        db.Employees.AddRange(employees);
        await db.SaveChangesAsync(cancellationToken);

        db.Compensation.AddRange(BuildCompensation(random, employees));
        db.HeadcountMonthly.AddRange(BuildHeadcount(random, regions, departments));
        db.AttritionEvents.AddRange(BuildAttrition(random, regions, departments));
        db.Programs.AddRange(BuildPrograms());

        var customers = BuildCustomers(random, regions);
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(cancellationToken);

        var orders = BuildOrders(random, customers);
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(cancellationToken);

        db.OrderItems.AddRange(BuildOrderItems(random, orders, products));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<Employee> BuildEmployees(Random random, Region[] regions, Department[] departments)
    {
        string[] first =
        [
            "Dana", "Marcus", "Priya", "Tomas", "Aisha", "Ingrid", "Omar", "Lena", "Kofi", "Mira",
            "Johan", "Yuki", "Rafael", "Nadia", "Elias", "Sofia", "Hassan", "Clara", "Dmitri", "Amara",
        ];
        string[] last =
        [
            "Okafor", "Lindqvist", "Raman", "Novak", "Haddad", "Berg", "Diallo", "Moreau", "Mensah",
            "Patel", "Virtanen", "Tanaka", "Costa", "Rahman", "Lindgren", "Rossi", "Yilmaz", "Dubois",
        ];

        var employees = new List<Employee>();

        // The three demo identities come first so their ids are stable and legible.
        employees.Add(MakeEmployee("Dana Okafor", Roles.Analyst, DemoPrincipals.Analyst,
            departments[1].Id, regions[0].Id, new DateOnly(2022, 3, 14)));
        employees.Add(MakeEmployee("Marcus Lindqvist", Roles.RegionalManager, DemoPrincipals.RegionalManager,
            departments[1].Id, regions[0].Id, new DateOnly(2021, 8, 2)));
        employees.Add(MakeEmployee("Priya Raman", Roles.HrDirector, DemoPrincipals.HrDirector,
            departments[3].Id, regions[1].Id, new DateOnly(2020, 1, 20)));

        for (var i = 0; i < 137; i++)
        {
            var name = $"{first[random.Next(first.Length)]} {last[random.Next(last.Length)]}";
            employees.Add(MakeEmployee(
                name,
                Roles.Analyst,
                principalKey: null,
                departments[random.Next(departments.Length)].Id,
                regions[random.Next(regions.Length)].Id,
                new DateOnly(2019 + random.Next(7), 1 + random.Next(12), 1 + random.Next(28))));
        }

        return employees;

        Employee MakeEmployee(string name, string role, string? principalKey, int departmentId, int regionId, DateOnly hired) =>
            new()
            {
                FullName = name,
                Email = name.ToLowerInvariant().Replace(' ', '.') + "@example.invalid",
                Role = role,
                PrincipalKey = principalKey,
                DepartmentId = departmentId,
                RegionId = regionId,
                IsActive = true,
                HiredOn = hired,
            };
    }

    private static IEnumerable<Compensation> BuildCompensation(Random random, List<Employee> employees) =>
        employees.Select(e => new Compensation
        {
            EmployeeId = e.Id,
            RegionId = e.RegionId,
            AnnualSalary = 48_000m + random.Next(0, 92) * 1_000m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

    private static IEnumerable<HeadcountMonthly> BuildHeadcount(Random random, Region[] regions, Department[] departments)
    {
        for (var monthsBack = 23; monthsBack >= 0; monthsBack--)
        {
            var month = new DateOnly(AsOf.Year, AsOf.Month, 1).AddMonths(-monthsBack);
            foreach (var department in departments)
            {
                foreach (var region in regions)
                {
                    yield return new HeadcountMonthly
                    {
                        DepartmentId = department.Id,
                        RegionId = region.Id,
                        Month = month,
                        Headcount = 18 + random.Next(0, 26),
                    };
                }
            }
        }
    }

    private static IEnumerable<AttritionEvent> BuildAttrition(Random random, Region[] regions, Department[] departments)
    {
        for (var i = 0; i < 184; i++)
        {
            var daysBack = random.Next(0, 24 * 30);
            yield return new AttritionEvent
            {
                DepartmentId = departments[random.Next(departments.Length)].Id,
                RegionId = regions[random.Next(regions.Length)].Id,
                EventDate = AsOf.AddDays(-daysBack),
                Reason = AttritionReasons[random.Next(AttritionReasons.Length)],
                ReplacementCost = 9_000m + random.Next(0, 42) * 500m,
            };
        }
    }

    private static IEnumerable<Entities.Program> BuildPrograms() =>
    [
        new() { Name = "Diversity & Inclusion (D&I)", FiscalYear = 2026, BudgetAmount = 208_620m, SpendAmount = 164_310m },
        new() { Name = "Diversity & Inclusion (D&I)", FiscalYear = 2025, BudgetAmount = 180_000m, SpendAmount = 176_420m },
        new() { Name = "Leadership Development", FiscalYear = 2026, BudgetAmount = 142_000m, SpendAmount = 98_750m },
        new() { Name = "Graduate Intake", FiscalYear = 2026, BudgetAmount = 96_500m, SpendAmount = 91_200m },
        new() { Name = "Wellbeing", FiscalYear = 2026, BudgetAmount = 64_000m, SpendAmount = 38_900m },
    ];

    /// <summary>
    /// A few reports per person so the gallery isn't empty.
    ///
    /// <para>
    /// These are owned rows: the policy on <c>saved_reports</c> is owner-only, so each principal
    /// sees their own three and nobody else's. The gallery demonstrates that without saying so.
    /// </para>
    /// </summary>
    private static IEnumerable<SavedReport> BuildSavedReports()
    {
        (string Owner, string Title, string Question)[] reports =
        [
            (DemoPrincipals.Analyst, "Revenue by region", "revenue by region, last quarter"),
            (DemoPrincipals.Analyst, "Revenue over time", "revenue by month"),
            (DemoPrincipals.Analyst, "What sells", "revenue by product category"),
            (DemoPrincipals.RegionalManager, "My region's revenue", "revenue by region, last quarter"),
            (DemoPrincipals.RegionalManager, "Headcount", "headcount by department"),
            (DemoPrincipals.RegionalManager, "Why people leave", "attrition by reason"),
            (DemoPrincipals.HrDirector, "D&I feasibility", "turnover cost vs the D&I program budget"),
            (DemoPrincipals.HrDirector, "Pay by department", "average salary by department"),
            (DemoPrincipals.HrDirector, "Why people leave", "attrition by reason"),
        ];

        var created = new DateTime(2026, 10, 28, 9, 30, 0, DateTimeKind.Utc);

        foreach (var (owner, title, question) in reports)
        {
            created = created.AddMinutes(37);
            yield return new SavedReport
            {
                Id = Guid.NewGuid(),
                OwnerKey = owner,
                Title = title,
                Question = question,
                // The spec is re-planned on open rather than replayed, so this is a record of
                // what was asked, not a cached answer that could silently go stale.
                SpecJson = "{}",
                CreatedAt = created,
                UpdatedAt = created,
            };
        }
    }

    private static List<Customer> BuildCustomers(Random random, Region[] regions)
    {
        string[] prefixes = ["Northwind", "Harbour", "Granite", "Juniper", "Meridian", "Copper", "Lantern", "Falcon"];
        string[] suffixes = ["Logistics", "Media", "Health", "Foods", "Energy", "Labs", "Partners", "Group"];

        var customers = new List<Customer>();
        for (var i = 0; i < 64; i++)
        {
            customers.Add(new Customer
            {
                Name = $"{prefixes[i % prefixes.Length]} {suffixes[random.Next(suffixes.Length)]}",
                RegionId = regions[random.Next(regions.Length)].Id,
            });
        }
        return customers;
    }

    private static List<Order> BuildOrders(Random random, List<Customer> customers)
    {
        var orders = new List<Order>();
        for (var i = 0; i < 920; i++)
        {
            var customer = customers[random.Next(customers.Count)];
            var daysBack = random.Next(0, 24 * 30);

            orders.Add(new Order
            {
                CustomerId = customer.Id,
                // Orders belong to the customer's region, so the two never disagree — a
                // mismatch would make the RLS predicate on order_items quietly wrong.
                RegionId = customer.RegionId,
                OrderedAt = AsOf.AddDays(-daysBack).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
                Status = OrderStatuses[random.Next(OrderStatuses.Length)],
            });
        }
        return orders;
    }

    private static IEnumerable<OrderItem> BuildOrderItems(Random random, List<Order> orders, Product[] products)
    {
        foreach (var order in orders)
        {
            var lines = 1 + random.Next(3);
            for (var i = 0; i < lines; i++)
            {
                var product = products[random.Next(products.Length)];
                yield return new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = product.Id,
                    RegionId = order.RegionId,
                    Quantity = 1 + random.Next(6),
                    UnitPrice = product.UnitPrice,
                };
            }
        }
    }
}

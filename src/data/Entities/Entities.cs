namespace SmartReport.Data.Entities;

/// <summary>
/// Roles a client's own people can hold. The source of truth for <c>app.role</c>.
/// </summary>
public static class ClientRoles
{
    /// <summary>Sees operations. Billing is not theirs.</summary>
    public const string Coordinator = "coordinator";

    /// <summary>Everything the coordinator sees, plus fees and invoices.</summary>
    public const string AccountOwner = "account_owner";
}

public static class Channels
{
    public const string Shopify = "Shopify";
    public const string Amazon = "Amazon";
    public const string Wholesale = "Wholesale";
    public const string TikTokShop = "TikTok Shop";

    public static readonly string[] All = [Shopify, Amazon, Wholesale, TikTokShop];
}

public static class Carriers
{
    public static readonly string[] All =
        ["UPS Ground", "FedEx Home", "USPS Ground Adv.", "DHL eCommerce", "OnTrac"];
}

/// <summary>A brand Northpoint fulfills for. The tenant boundary.</summary>
public sealed class Client
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateOnly JoinedOn { get; set; }
}

/// <summary>
/// A person at a client. <see cref="Role"/> is read from here on every request — never from the
/// session cookie and never from anything the model produced (A5).
/// </summary>
public sealed class ClientUser
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string JobTitle { get; set; }

    /// <summary>Stable key the session carries. Null for everyone the demo can't sign in as.</summary>
    public string? PrincipalKey { get; set; }

    public bool IsActive { get; set; }
}

public sealed class Product
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int OnHand { get; set; }
    public int ReorderPoint { get; set; }
}

public sealed class Order
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public required string Channel { get; set; }
    public DateTime OrderedAt { get; set; }

    /// <summary>What Northpoint committed to. Late is measured against this, not against a guess.</summary>
    public DateTime PromisedShipAt { get; set; }

    public DateTime? ShippedAt { get; set; }
    public required string Status { get; set; }
}

public sealed class OrderItem
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public long OrderId { get; set; }
    public long ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class Shipment
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public long OrderId { get; set; }
    public required string Carrier { get; set; }
    public required string Service { get; set; }
    public DateTime ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Settled at seed time against the order's promise, so "late" means one thing everywhere.</summary>
    public bool IsLate { get; set; }

    /// <summary>What the client was billed. Not what Northpoint paid — see <see cref="CarrierContractRate"/>.</summary>
    public decimal BilledAmount { get; set; }
}

public sealed class Return
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public long OrderId { get; set; }
    public required string Reason { get; set; }
    public DateTime ReceivedAt { get; set; }
    public decimal RefundAmount { get; set; }
}

/// <summary>A line on the client's monthly invoice. Restricted to the account owner.</summary>
public sealed class Fee
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public DateOnly PeriodMonth { get; set; }
    public required string FeeType { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// What Northpoint pays its carriers.
///
/// <para>
/// <b>Deliberately has no <c>ClientId</c>.</b> This is Northpoint's own commercial data: it sits in
/// the same database the client portal reads, and no client-facing role may select it. A client
/// asking about it is redirected to what they were billed, which is theirs to see.
/// </para>
/// </summary>
public sealed class CarrierContractRate
{
    public long Id { get; set; }
    public required string Carrier { get; set; }
    public required string Service { get; set; }
    public decimal CostPerLabel { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

/// <summary>
/// Anonymized cross-client aggregates, pre-computed.
///
/// <para>
/// <b>Deliberately has no <c>ClientId</c>.</b> There is no join from this table to any client's
/// rows, so a benchmark cannot be unpicked into the clients behind it. <see cref="ClientCount"/>
/// is the cohort size, and every query against this table carries a minimum-cohort predicate —
/// see the <c>benchmarks</c> model in the catalog and the policy on this table.
/// </para>
/// </summary>
public sealed class BenchmarkMetric
{
    public long Id { get; set; }
    public required string MetricKey { get; set; }
    public DateOnly PeriodMonth { get; set; }
    public required string DimensionKey { get; set; }
    public required string DimensionValue { get; set; }

    /// <summary>How many clients are behind this row. Below the floor, it is not readable.</summary>
    public int ClientCount { get; set; }

    public decimal P25 { get; set; }
    public decimal P50 { get; set; }
    public decimal P75 { get; set; }
}

/// <summary>A starter question in the Library. Global: the same list for every client.</summary>
public sealed class ReportTemplate
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Question { get; set; }
    public required string Category { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A report someone kept. Scoped to the client, and to the person who saved it.</summary>
public sealed class SavedReport
{
    public Guid Id { get; set; }
    public int ClientId { get; set; }
    public required string OwnerKey { get; set; }
    public required string Title { get; set; }
    public required string Question { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

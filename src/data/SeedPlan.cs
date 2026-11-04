namespace SmartReport.Data;

/// <summary>
/// The shape of the demo data, written down.
///
/// <para>
/// These figures are chosen so the default October report reads exactly as designed: 8,412 orders
/// shipped, 97.2% on time, 1.4 days to ship, six low-stock products. Everything downstream is
/// derived from real rows and real aggregation — the numbers are planned, not hard-coded into the
/// UI, so changing one here changes the report.
/// </para>
/// </summary>
internal static class SeedPlan
{
    public const int Seed = 20261103;

    /// <summary>The demo's "today". Fixed so a screenshot stays true next month.</summary>
    public static readonly DateOnly AsOf = new(2026, 10, 31);

    public const string TrailheadSlug = "trailhead-coffee";
    public const string BluefinSlug = "bluefin-apparel";

    /// <summary>Orders shipped per month for Trailhead, 2025 then 2026. Drives the trend chart.</summary>
    public static readonly int[] Volume2025 =
        [4200, 3950, 4300, 4600, 4800, 4900, 5100, 5400, 5800, 6300];

    public static readonly int[] Volume2026 =
        [5100, 4800, 5500, 5900, 6200, 6000, 6600, 7100, 7696, 8412];

    /// <summary>Share of orders by channel. Must sum to 1.</summary>
    public static readonly (string Channel, double Share)[] ChannelMix =
    [
        (Entities.Channels.Shopify, 0.61),
        (Entities.Channels.Amazon, 0.24),
        (Entities.Channels.Wholesale, 0.09),
        (Entities.Channels.TikTokShop, 0.06),
    ];

    /// <summary>Share of shipments by carrier. Must sum to 1.</summary>
    public static readonly (string Carrier, string Service, double Share, decimal Billed)[] CarrierMix =
    [
        ("UPS Ground", "Ground", 0.38, 8.40m),
        ("FedEx Home", "Home Delivery", 0.24, 7.95m),
        ("USPS Ground Adv.", "Ground Advantage", 0.20, 6.10m),
        ("DHL eCommerce", "Expedited Max", 0.12, 5.75m),
        ("OnTrac", "Ground", 0.06, 6.80m),
    ];

    /// <summary>
    /// Late shipments in the headline month, by carrier. 236 of 8,412 is 97.2% on time — the
    /// figure the KPI shows, computed from these rows rather than asserted.
    /// </summary>
    public static readonly (string Carrier, int Late)[] OctoberLate =
    [
        ("UPS Ground", 112),
        ("FedEx Home", 61),
        ("USPS Ground Adv.", 39),
        ("DHL eCommerce", 16),
        ("OnTrac", 8),
    ];

    /// <summary>Northpoint promises to ship within three days. "Late" means past this, nothing else.</summary>
    public const int PromiseDays = 3;

    /// <summary>
    /// Ship delays for on-time orders, in days, with weights. Chosen so that blending these with
    /// the ~2.8% that run late averages out to the 1.4 days the KPI reports.
    /// </summary>
    public static readonly (double Days, double Weight)[] OnTimeDelay =
        [(0.4, 0.19), (0.9, 0.27), (1.4, 0.24), (2.0, 0.20), (2.6, 0.10)];

    public static readonly (string Reason, double Weight)[] ReturnReasons =
    [
        ("Changed mind", 0.31), ("Arrived damaged", 0.22), ("Wrong item sent", 0.17),
        ("Not as described", 0.15), ("Arrived late", 0.09), ("Duplicate order", 0.06),
    ];

    /// <summary>Roughly 3% of orders come back.</summary>
    public const double ReturnRate = 0.031;

    public static readonly string[] FeeTypes =
        ["Storage", "Pick & pack", "Shipping", "Returns handling", "Account management"];

    /// <summary>
    /// The benchmark cohort.
    ///
    /// <para>
    /// 24 clients now, but Northpoint had only 7 in early 2025 — so those months fall below the
    /// floor and are not readable. That is deliberate: the demo shows the safeguard both
    /// satisfied and refusing.
    /// </para>
    /// </summary>
    public const int CohortNow = 24;
    public const int CohortEarly2025 = 7;

    /// <summary>Months before this have the small cohort.</summary>
    public static readonly DateOnly CohortGrewOn = new(2025, 4, 1);

    public static readonly string[] OtherClientNames =
    [
        "Bluefin Apparel", "Kestrel Outdoors", "Marigold Home", "Northgate Pet",
        "Saltbox Ceramics", "Vellum Stationery", "Juniper Skincare", "Tidepool Toys",
        "Foundry Coffee Gear", "Lark & Finch", "Copperleaf Tea", "Harbour Candles",
        "Mendel Supplements", "Orchard Row Jams", "Pinnacle Cycling", "Quarry Denim",
        "Rivet Hardware", "Sable Fragrance", "Thistle Linens", "Umber Leather",
        "Verdant Plants", "Wren Audio", "Yarrow Botanicals",
    ];
}

namespace SmartReport.Data.Entities;

// Reference data. Shared across every principal (D3): a region's name is not a secret.
public sealed class Region
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Department
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public decimal UnitPrice { get; set; }
}

// Operational data. Region-scoped by row-level security.
public sealed class Employee
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public int DepartmentId { get; set; }
    public int RegionId { get; set; }

    /// <summary>Authorization role. The source of truth for <c>app.role</c> (A5).</summary>
    public required string Role { get; set; }

    /// <summary>Stable key used by the dev sign-in switcher. Null for everyone else.</summary>
    public string? PrincipalKey { get; set; }

    public bool IsActive { get; set; }
    public DateOnly HiredOn { get; set; }
}

public sealed class Compensation
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int RegionId { get; set; }
    public decimal AnnualSalary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class HeadcountMonthly
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public int RegionId { get; set; }
    public DateOnly Month { get; set; }
    public int Headcount { get; set; }
}

public sealed class AttritionEvent
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public int RegionId { get; set; }
    public DateOnly EventDate { get; set; }
    public required string Reason { get; set; }
    public decimal ReplacementCost { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int RegionId { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int RegionId { get; set; }
    public DateTime OrderedAt { get; set; }
    public required string Status { get; set; }
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }

    /// <summary>
    /// Denormalised from the parent order so row-level security can scope line items directly.
    /// A policy that had to join back to <c>orders</c> would be both slower and easier to get
    /// wrong (D2).
    /// </summary>
    public int RegionId { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Program
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int FiscalYear { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal SpendAmount { get; set; }
}

/// <summary>A report someone kept. Owner-only, enforced by RLS on <c>owner_key</c>.</summary>
public sealed class SavedReport
{
    public Guid Id { get; set; }
    public required string OwnerKey { get; set; }
    public required string Title { get; set; }
    public required string Question { get; set; }
    public required string SpecJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

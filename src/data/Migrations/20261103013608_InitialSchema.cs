using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartReport.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attrition_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    replacement_cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attrition_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compensation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    annual_salary = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compensation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    principal_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "headcount_monthly",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_headcount_monthly", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    region_id = table.Column<int>(type: "integer", nullable: false),
                    ordered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "programs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    budget_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    spend_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    spec_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_reports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attrition_events_region_id_event_date",
                table: "attrition_events",
                columns: new[] { "region_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "IX_compensation_employee_id",
                table: "compensation",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_compensation_region_id",
                table: "compensation",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_customers_region_id",
                table: "customers",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_employees_principal_key",
                table: "employees",
                column: "principal_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_region_id",
                table: "employees",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_headcount_monthly_region_id_month",
                table: "headcount_monthly",
                columns: new[] { "region_id", "month" });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_region_id",
                table: "order_items",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_region_id_ordered_at",
                table: "orders",
                columns: new[] { "region_id", "ordered_at" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_reports_owner_key",
                table: "saved_reports",
                column: "owner_key");

            // Row-level security, in the same migration that creates the tables (D1).
            migrationBuilder.Sql(RowLevelSecurity.Up());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RowLevelSecurity.Down());

            migrationBuilder.DropTable(
                name: "attrition_events");

            migrationBuilder.DropTable(
                name: "compensation");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "headcount_monthly");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "programs");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "saved_reports");
        }
    }
}

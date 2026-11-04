using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReport.Data.Migrations
{
    /// <inheritdoc />
    public partial class NorthpointSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benchmark_metrics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    metric_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    dimension_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    dimension_value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    client_count = table.Column<int>(type: "integer", nullable: false),
                    p25 = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    p50 = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    p75 = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_benchmark_metrics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_contract_rates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    carrier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cost_per_label = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_contract_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    job_title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    principal_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fees",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    fee_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    promised_ship_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    shipped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    reorder_point = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    question = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "returns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_returns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    owner_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    carrier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    shipped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_late = table.Column<bool>(type: "boolean", nullable: false),
                    billed_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_benchmark_metrics_metric_key_period_month",
                table: "benchmark_metrics",
                columns: new[] { "metric_key", "period_month" });

            migrationBuilder.CreateIndex(
                name: "IX_client_users_client_id",
                table: "client_users",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_users_principal_key",
                table: "client_users",
                column: "principal_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clients_slug",
                table: "clients",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fees_client_id_period_month",
                table: "fees",
                columns: new[] { "client_id", "period_month" });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_client_id_order_id",
                table: "order_items",
                columns: new[] { "client_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_client_id_ordered_at",
                table: "orders",
                columns: new[] { "client_id", "ordered_at" });

            migrationBuilder.CreateIndex(
                name: "IX_products_client_id",
                table: "products",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_returns_client_id_received_at",
                table: "returns",
                columns: new[] { "client_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_reports_client_id_owner_key",
                table: "saved_reports",
                columns: new[] { "client_id", "owner_key" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_client_id_shipped_at",
                table: "shipments",
                columns: new[] { "client_id", "shipped_at" });

            // Row-level security, in the same migration that creates the tables (D1).
            migrationBuilder.Sql(RowLevelSecurity.Up());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RowLevelSecurity.Down());

            migrationBuilder.DropTable(
                name: "benchmark_metrics");

            migrationBuilder.DropTable(
                name: "carrier_contract_rates");

            migrationBuilder.DropTable(
                name: "client_users");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "fees");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "report_templates");

            migrationBuilder.DropTable(
                name: "returns");

            migrationBuilder.DropTable(
                name: "saved_reports");

            migrationBuilder.DropTable(
                name: "shipments");
        }
    }
}

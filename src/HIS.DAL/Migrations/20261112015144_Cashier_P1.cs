using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    public partial class Cashier_P1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "seq_invoice_number");

            migrationBuilder.CreateSequence<int>(
                name: "seq_transaction_code");

            migrationBuilder.AddColumn<bool>(
                name: "is_insurance_covered",
                table: "services",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "payments",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_code",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shift_id",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CONFIRMED");

            migrationBuilder.AddColumn<string>(
                name: "transaction_code",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_insurance_covered",
                table: "medications",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "total_amount",
                table: "invoices",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "patient_amount",
                table: "invoices",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "insurance_covered_amount",
                table: "invoices",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exam_date",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "invoice_items",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "coverage_pct_applied",
                table: "invoice_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "covered_amount",
                table: "invoice_items",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "is_insurance_covered",
                table: "invoice_items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "medication_id",
                table: "invoice_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "patient_amount",
                table: "invoice_items",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "invoice_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "service_id",
                table: "invoice_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "invoice_items",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "cashier_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    cashier_id = table.Column<int>(type: "integer", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    opening_cash = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false, defaultValue: 0m),
                    expected_cash = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: true),
                    counted_cash = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "OPEN"),
                    note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_cashier_shifts_accounts_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[] { 6, "ACCOUNTANT", null, "Kế toán / Thu ngân" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_idempotency_key",
                table: "payments",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_shift_id",
                table: "payments",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_transaction_code",
                table: "payments",
                column: "transaction_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_cashier_shifts_cashier_status",
                table: "cashier_shifts",
                columns: new[] { "cashier_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_payments_cashier_shifts_shift_id",
                table: "payments",
                column: "shift_id",
                principalTable: "cashier_shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_cashier_shifts_shift_id",
                table: "payments");

            migrationBuilder.DropTable(
                name: "cashier_shifts");

            migrationBuilder.DropIndex(
                name: "ix_payments_idempotency_key",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_shift_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_transaction_code",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: 6);

            migrationBuilder.DropColumn(
                name: "is_insurance_covered",
                table: "services");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reference_code",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "shift_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "transaction_code",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "is_insurance_covered",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "exam_date",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "coverage_pct_applied",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "covered_amount",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "is_insurance_covered",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "medication_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "patient_amount",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "invoice_items");

            migrationBuilder.DropSequence(
                name: "seq_invoice_number");

            migrationBuilder.DropSequence(
                name: "seq_transaction_code");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "payments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)",
                oldPrecision: 18,
                oldScale: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "total_amount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)",
                oldPrecision: 18,
                oldScale: 0,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "patient_amount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)",
                oldPrecision: 18,
                oldScale: 0,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "insurance_covered_amount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)",
                oldPrecision: 18,
                oldScale: 0,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount",
                table: "invoice_items",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,0)",
                oldPrecision: 18,
                oldScale: 0);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    public partial class Cashier_P3_DepositRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.CreateSequence<int>(
                name: "seq_deposit_code");

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                table: "payments",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<int>(
                name: "invoice_id",
                table: "payments",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "deposit_id",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "original_payment_id",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_reason",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deposits",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    deposit_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    patient_id = table.Column<int>(type: "integer", nullable: false),
                    appointment_id = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false),
                    used_amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false, defaultValue: 0m),
                    refunded_amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    purpose = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    approved_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cashier_id = table.Column<int>(type: "integer", nullable: true),
                    shift_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposits", x => x.id);
                    table.CheckConstraint("ck_deposits_amount", "amount > 0");
                    table.CheckConstraint("ck_deposits_refunded_amount", "refunded_amount >= 0");
                    table.CheckConstraint("ck_deposits_status", "status IN ('ACTIVE','DEPLETED','REFUNDED')");
                    table.CheckConstraint("ck_deposits_used_amount", "used_amount >= 0");
                    table.ForeignKey(
                        name: "fk_deposits_accounts_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposits_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposits_cashier_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "cashier_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposits_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposit_usages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    deposit_id = table.Column<int>(type: "integer", nullable: false),
                    invoice_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    cashier_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposit_usages", x => x.id);
                    table.CheckConstraint("ck_deposit_usages_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_deposit_usages_accounts_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposit_usages_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_deposit_usages_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_payments_deposit",
                table: "payments",
                column: "deposit_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_original_payment_id",
                table: "payments",
                column: "original_payment_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH','BANK_TRANSFER','DEPOSIT_DEDUCTION')");

            migrationBuilder.CreateIndex(
                name: "idx_deposit_usages_deposit",
                table: "deposit_usages",
                column: "deposit_id");

            migrationBuilder.CreateIndex(
                name: "idx_deposit_usages_invoice",
                table: "deposit_usages",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposit_usages_cashier_id",
                table: "deposit_usages",
                column: "cashier_id");

            migrationBuilder.CreateIndex(
                name: "idx_deposits_appointment",
                table: "deposits",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "idx_deposits_patient",
                table: "deposits",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposits_cashier_id",
                table: "deposits",
                column: "cashier_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposits_deposit_code",
                table: "deposits",
                column: "deposit_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposits_shift_id",
                table: "deposits",
                column: "shift_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_deposits_deposit_id",
                table: "payments",
                column: "deposit_id",
                principalTable: "deposits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_payments_original_payment_id",
                table: "payments",
                column: "original_payment_id",
                principalTable: "payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_deposits_deposit_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_payments_original_payment_id",
                table: "payments");

            migrationBuilder.DropTable(
                name: "deposit_usages");

            migrationBuilder.DropTable(
                name: "deposits");

            migrationBuilder.DropIndex(
                name: "idx_payments_deposit",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_original_payment_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deposit_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "original_payment_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_reason",
                table: "payments");

            migrationBuilder.DropSequence(
                name: "seq_deposit_code");

            migrationBuilder.Sql("DELETE FROM payments WHERE invoice_id IS NULL OR payment_method = 'DEPOSIT_DEDUCTION';");

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25);

            migrationBuilder.AlterColumn<int>(
                name: "invoice_id",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH','BANK_TRANSFER')");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    public partial class QueuePositionNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_counters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_slots",
                table: "doctor_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_priority_level",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointment_status_history_performed_by",
                table: "appointment_status_history");

            migrationBuilder.DropColumn(
                name: "max_patients",
                table: "doctor_schedules");

            migrationBuilder.DropColumn(
                name: "online_slots",
                table: "doctor_schedules");

            migrationBuilder.AlterColumn<int>(
                name: "slot_duration_minutes",
                table: "doctor_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 60,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "online_ratio_pct",
                table: "doctor_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "overtaken_count",
                table: "appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "slot_position",
                table: "appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_late_cancel",
                table: "appointment_status_history",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "online_booked",
                table: "appointment_slots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "online_quota",
                table: "appointment_slots",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "booking_blocked_until",
                table: "accounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "no_show_reset_at",
                table: "accounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_doctor_schedules_online_ratio",
                table: "doctor_schedules",
                sql: "online_ratio_pct BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "uq_slot_position",
                table: "appointments",
                columns: new[] { "slot_id", "slot_position" },
                unique: true,
                filter: "slot_position IS NOT NULL AND status_id IN (1,2,3,4,5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_overtaken_count",
                table: "appointments",
                sql: "overtaken_count BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_priority_level",
                table: "appointments",
                sql: "priority_level BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_slot_position",
                table: "appointments",
                sql: "slot_position >= 1");

            migrationBuilder.CreateIndex(
                name: "idx_history_cancel",
                table: "appointment_status_history",
                columns: new[] { "performed_by", "changed_at" },
                filter: "is_late_cancel");

            // Slot cũ sinh theo mô hình 15 phút/suất nên capacity = 1, trong khi online_quota vừa thêm
            // nhận default 3 -> vi phạm ck_online_quota. Phải chuẩn hoá dữ liệu cũ trước khi ràng buộc có hiệu lực.
            migrationBuilder.Sql(@"
UPDATE appointment_slots s
SET online_booked = (SELECT count(*) FROM appointments a
                     WHERE a.slot_id = s.id
                       AND a.booking_channel = 'WEB'
                       AND a.status_id IN (1,2,3,4,5));

UPDATE appointment_slots s
SET online_quota = LEAST(s.capacity, GREATEST(
        ROUND(s.capacity * COALESCE((
            SELECT ds.online_ratio_pct FROM doctor_schedules ds
            WHERE ds.doctor_id = s.doctor_id
              AND ds.is_active
              AND ds.day_of_week = EXTRACT(DOW FROM s.start_time)::integer
              AND ds.effective_from <= s.start_time::date
              AND (ds.effective_to IS NULL OR ds.effective_to >= s.start_time::date)
            ORDER BY ds.id LIMIT 1), 60) / 100.0)::integer,
        s.online_booked));

UPDATE doctor_schedules SET slot_duration_minutes = 60 WHERE slot_duration_minutes <> 60;
");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_booked",
                table: "appointment_slots",
                sql: "online_booked <= online_quota AND online_booked <= booked_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_quota",
                table: "appointment_slots",
                sql: "online_quota <= capacity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_doctor_schedules_online_ratio",
                table: "doctor_schedules");

            migrationBuilder.DropIndex(
                name: "uq_slot_position",
                table: "appointments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_overtaken_count",
                table: "appointments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_priority_level",
                table: "appointments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointments_slot_position",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "idx_history_cancel",
                table: "appointment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_booked",
                table: "appointment_slots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_quota",
                table: "appointment_slots");

            migrationBuilder.DropColumn(
                name: "online_ratio_pct",
                table: "doctor_schedules");

            migrationBuilder.DropColumn(
                name: "overtaken_count",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "slot_position",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "is_late_cancel",
                table: "appointment_status_history");

            migrationBuilder.DropColumn(
                name: "online_booked",
                table: "appointment_slots");

            migrationBuilder.DropColumn(
                name: "online_quota",
                table: "appointment_slots");

            migrationBuilder.DropColumn(
                name: "booking_blocked_until",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "no_show_reset_at",
                table: "accounts");

            migrationBuilder.AlterColumn<int>(
                name: "slot_duration_minutes",
                table: "doctor_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 15,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "max_patients",
                table: "doctor_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "online_slots",
                table: "doctor_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 28);

            migrationBuilder.CreateTable(
                name: "queue_counters",
                columns: table => new
                {
                    queue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    room_id = table.Column<int>(type: "integer", nullable: false),
                    shift = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    channel = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queue_counters", x => new { x.queue_date, x.room_id, x.shift, x.channel });
                    table.CheckConstraint("ck_queue_counters_channel", "channel IN ('WEB','ON_SITE')");
                    table.CheckConstraint("ck_queue_counters_last_number", "last_number >= 0");
                    table.CheckConstraint("ck_queue_counters_shift", "shift IN ('MORNING','AFTERNOON')");
                    table.ForeignKey(
                        name: "fk_queue_counters_clinic_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "clinic_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_slots",
                table: "doctor_schedules",
                sql: "online_slots BETWEEN 0 AND max_patients");

            migrationBuilder.AddCheckConstraint(
                name: "ck_appointments_priority_level",
                table: "appointments",
                sql: "priority_level BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_performed_by",
                table: "appointment_status_history",
                column: "performed_by");

            migrationBuilder.CreateIndex(
                name: "ix_queue_counters_room_id",
                table: "queue_counters",
                column: "room_id");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_terminal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "insurance_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    coverage_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    referral_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insurance_plans", x => x.id);
                    table.CheckConstraint("ck_insurance_plans_referral_type", "referral_type IN ('IN_NETWORK','OUT_OF_NETWORK')");
                });

            migrationBuilder.CreateTable(
                name: "medications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    active_ingredient = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    strength = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    national_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                    table.CheckConstraint("ck_patients_gender", "gender IN ('MALE','FEMALE','OTHER')");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clinic_rooms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    room_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    room_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_clinic_rooms_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "insurance_cards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    patient_id = table.Column<int>(type: "integer", nullable: false),
                    card_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    insurance_plan_id = table.Column<int>(type: "integer", nullable: false),
                    registered_facility = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_registered_here = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insurance_cards", x => x.id);
                    table.CheckConstraint("ck_insurance_dates", "valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_insurance_cards_insurance_plans_insurance_plan_id",
                        column: x => x.insurance_plan_id,
                        principalTable: "insurance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_insurance_cards_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_email_lower", "email IS NULL OR email = lower(email)");
                    table.ForeignKey(
                        name: "fk_accounts_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    service_type_id = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_services_service_types_service_type_id",
                        column: x => x.service_type_id,
                        principalTable: "service_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "account_patient_profiles",
                columns: table => new
                {
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    patient_id = table.Column<int>(type: "integer", nullable: false),
                    relationship = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "ACTIVE"),
                    can_view_records = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<int>(type: "integer", nullable: true),
                    verified_by = table.Column<int>(type: "integer", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_patient_profiles", x => new { x.account_id, x.patient_id });
                    table.CheckConstraint("ck_account_patient_profiles_relationship", "relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER')");
                    table.CheckConstraint("ck_account_patient_profiles_status", "status IN ('ACTIVE','PENDING','REVOKED')");
                    table.CheckConstraint("ck_link_adult_records_consent", "can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL");
                    table.CheckConstraint("ck_link_pending_no_records", "status = 'ACTIVE' OR can_view_records = FALSE");
                    table.CheckConstraint("ck_link_self_not_pending", "NOT (relationship = 'SELF' AND status = 'PENDING')");
                    table.ForeignKey(
                        name: "fk_account_patient_profiles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_patient_profiles_accounts_created_by",
                        column: x => x.created_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_patient_profiles_accounts_verified_by",
                        column: x => x.verified_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_patient_profiles_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "department_heads",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_heads", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_heads_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_department_heads_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctors",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    license_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    avg_consultation_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctors_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctors_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duty_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    assigned_by = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duty_shifts", x => x.id);
                    table.CheckConstraint("ck_duty_time", "end_time > start_time");
                    table.ForeignKey(
                        name: "fk_duty_shifts_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duty_shifts_accounts_assigned_by",
                        column: x => x.assigned_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_duty_shifts_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nurses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: true),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nurses", x => x.id);
                    table.ForeignKey(
                        name: "fk_nurses_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nurses_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_slots",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    doctor_id = table.Column<int>(type: "integer", nullable: false),
                    room_id = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    booked_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, defaultValue: "AVAILABLE"),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_slots", x => x.id);
                    table.CheckConstraint("ck_appointment_slots_status", "status IN ('AVAILABLE','FULL','BLOCKED')");
                    table.CheckConstraint("ck_capacity", "booked_count <= capacity");
                    table.ForeignKey(
                        name: "fk_appointment_slots_clinic_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "clinic_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_slots_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    doctor_id = table.Column<int>(type: "integer", nullable: false),
                    room_id = table.Column<int>(type: "integer", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    shift = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    slot_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    max_patients = table.Column<int>(type: "integer", nullable: false, defaultValue: 40),
                    online_slots = table.Column<int>(type: "integer", nullable: false, defaultValue: 28),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_schedules", x => x.id);
                    table.CheckConstraint("ck_doctor_schedules_day_of_week", "day_of_week BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_doctor_schedules_shift", "shift IN ('MORNING','AFTERNOON')");
                    table.CheckConstraint("ck_online_slots", "online_slots BETWEEN 0 AND max_patients");
                    table.ForeignKey(
                        name: "fk_doctor_schedules_clinic_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "clinic_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_schedules_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_exceptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    doctor_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "PENDING"),
                    replacement_doctor_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    approved_by = table.Column<int>(type: "integer", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_exceptions", x => x.id);
                    table.CheckConstraint("ck_exception_approved", "status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_exception_replacement", "replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id");
                    table.CheckConstraint("ck_schedule_exceptions_shift", "shift IN ('MORNING','AFTERNOON')");
                    table.CheckConstraint("ck_schedule_exceptions_status", "status IN ('PENDING','APPROVED','REJECTED')");
                    table.CheckConstraint("ck_schedule_exceptions_type", "type IN ('LEAVE','TRAINING','SURGERY','SHIFT_SWAP')");
                    table.ForeignKey(
                        name: "fk_schedule_exceptions_accounts_approved_by",
                        column: x => x.approved_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_exceptions_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_exceptions_doctors_replacement_doctor_id",
                        column: x => x.replacement_doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    patient_id = table.Column<int>(type: "integer", nullable: false),
                    booked_by = table.Column<int>(type: "integer", nullable: true),
                    slot_id = table.Column<int>(type: "integer", nullable: true),
                    doctor_id = table.Column<int>(type: "integer", nullable: true),
                    department_id = table.Column<int>(type: "integer", nullable: true),
                    room_id = table.Column<int>(type: "integer", nullable: true),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    booking_channel = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, defaultValue: "WEB"),
                    visit_type = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, defaultValue: "SCHEDULED"),
                    priority_level = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    queue_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    hold_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    checked_in_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    called_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    parent_appointment_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_booking_channel", "booking_channel IN ('WEB','ON_SITE')");
                    table.CheckConstraint("ck_appointments_priority_level", "priority_level BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_appointments_visit_type", "visit_type IN ('SCHEDULED','WALK_IN')");
                    table.ForeignKey(
                        name: "fk_appointments_accounts_booked_by",
                        column: x => x.booked_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_appointment_slots_slot_id",
                        column: x => x.slot_id,
                        principalTable: "appointment_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_appointment_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "appointment_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_appointments_parent_appointment_id",
                        column: x => x.parent_appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_clinic_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "clinic_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_id = table.Column<int>(type: "integer", nullable: false),
                    old_status_id = table.Column<int>(type: "integer", nullable: true),
                    new_status_id = table.Column<int>(type: "integer", nullable: false),
                    performed_by = table.Column<int>(type: "integer", nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_accounts_performed_by",
                        column: x => x.performed_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointment_statuses_new_status_",
                        column: x => x.new_status_id,
                        principalTable: "appointment_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointment_statuses_old_status_",
                        column: x => x.old_status_id,
                        principalTable: "appointment_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_id = table.Column<int>(type: "integer", nullable: false),
                    patient_id = table.Column<int>(type: "integer", nullable: false),
                    insurance_plan_id = table.Column<int>(type: "integer", nullable: true),
                    insurance_card_id = table.Column<int>(type: "integer", nullable: true),
                    coverage_pct_applied = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    insurance_covered_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    patient_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "UNPAID"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_coverage_pct_applied", "coverage_pct_applied BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_invoices_status", "status IN ('UNPAID','PARTIALLY_PAID','PAID')");
                    table.ForeignKey(
                        name: "fk_invoices_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_insurance_cards_insurance_card_id",
                        column: x => x.insurance_card_id,
                        principalTable: "insurance_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_insurance_plans_insurance_plan_id",
                        column: x => x.insurance_plan_id,
                        principalTable: "insurance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medical_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_id = table.Column<int>(type: "integer", nullable: false),
                    doctor_id = table.Column<int>(type: "integer", nullable: false),
                    symptoms = table.Column<string>(type: "text", nullable: true),
                    clinical_examination = table.Column<string>(type: "text", nullable: true),
                    primary_diagnosis_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    primary_diagnosis_desc = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    secondary_diagnosis = table.Column<string>(type: "text", nullable: true),
                    conclusion = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medical_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_medical_records_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medical_records_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    appointment_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vital_signs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_id = table.Column<int>(type: "integer", nullable: false),
                    pulse = table.Column<int>(type: "integer", nullable: true),
                    systolic_bp = table.Column<int>(type: "integer", nullable: true),
                    diastolic_bp = table.Column<int>(type: "integer", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    respiratory_rate = table.Column<int>(type: "integer", nullable: true),
                    spo2 = table.Column<int>(type: "integer", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    height = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    recorded_by = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vital_signs", x => x.id);
                    table.ForeignKey(
                        name: "fk_vital_signs_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vital_signs_nurses_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "nurses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    invoice_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_items", x => x.id);
                    table.CheckConstraint("ck_invoice_items_category", "category IN ('EXAMINATION','MEDICATION','SERVICE')");
                    table.ForeignKey(
                        name: "fk_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    invoice_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payment_type = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, defaultValue: "PAYMENT"),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cashier_id = table.Column<int>(type: "integer", nullable: true),
                    receipt_email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_payment_method", "payment_method IN ('CASH','BANK_TRANSFER')");
                    table.CheckConstraint("ck_payments_payment_type", "payment_type IN ('DEPOSIT','PAYMENT','REFUND')");
                    table.ForeignKey(
                        name: "fk_payments_accounts_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    patient_id = table.Column<int>(type: "integer", nullable: true),
                    medical_record_id = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_logs_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_logs_medical_records_medical_record_id",
                        column: x => x.medical_record_id,
                        principalTable: "medical_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_logs_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    medical_record_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_orders", x => x.id);
                    table.CheckConstraint("ck_lab_orders_status", "status IN ('PENDING','COMPLETED')");
                    table.ForeignKey(
                        name: "fk_lab_orders_medical_records_medical_record_id",
                        column: x => x.medical_record_id,
                        principalTable: "medical_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lab_orders_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    medical_record_id = table.Column<int>(type: "integer", nullable: false),
                    prescribed_date = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_prescriptions_medical_records_medical_record_id",
                        column: x => x.medical_record_id,
                        principalTable: "medical_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_results",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "text", nullable: true),
                    performed_by = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_results_accounts_performed_by",
                        column: x => x.performed_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lab_results_lab_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "lab_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescription_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    prescription_id = table.Column<int>(type: "integer", nullable: false),
                    medication_id = table.Column<int>(type: "integer", nullable: false),
                    dosage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: true),
                    usage_instructions = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_prescription_items_medications_medication_id",
                        column: x => x.medication_id,
                        principalTable: "medications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescription_items_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalTable: "prescriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "appointment_statuses",
                columns: new[] { "id", "code", "name", "sort_order" },
                values: new object[,]
                {
                    { 1, "PENDING", "Chờ xác nhận", 1 },
                    { 2, "CONFIRMED", "Đã xác nhận", 2 },
                    { 3, "CHECKED_IN", "Đã check-in", 3 },
                    { 4, "READY_FOR_DOCTOR", "Chờ bác sĩ", 4 },
                    { 5, "IN_PROGRESS", "Đang khám", 5 }
                });

            migrationBuilder.InsertData(
                table: "appointment_statuses",
                columns: new[] { "id", "code", "is_terminal", "name", "sort_order" },
                values: new object[,]
                {
                    { 6, "COMPLETED", true, "Hoàn thành", 6 },
                    { 7, "CANCELLED", true, "Đã hủy", 7 },
                    { 8, "NO_SHOW", true, "Không đến", 8 },
                    { 9, "EXPIRED", true, "Hết hạn giữ chỗ", 9 }
                });

            migrationBuilder.InsertData(
                table: "appointment_statuses",
                columns: new[] { "id", "code", "name", "sort_order" },
                values: new object[] { 10, "RESCHEDULE_REQUIRED", "Cần đặt lại lịch", 10 });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[,]
                {
                    { 1, "ADMIN", null, "Quản trị viên" },
                    { 2, "DEPARTMENT_HEAD", null, "Trưởng khoa" },
                    { 3, "DOCTOR", null, "Bác sĩ" },
                    { 4, "NURSE", null, "Y tá / Lễ tân" },
                    { 5, "PATIENT", null, "Bệnh nhân" }
                });

            migrationBuilder.InsertData(
                table: "service_types",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { 1, "EXAMINATION", "Khám bệnh" },
                    { 2, "LAB_TEST", "Xét nghiệm" },
                    { 3, "IMAGING", "Chẩn đoán hình ảnh" }
                });

            migrationBuilder.CreateIndex(
                name: "idx_access_logs_account",
                table: "access_logs",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "idx_access_logs_patient",
                table: "access_logs",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_logs_medical_record_id",
                table: "access_logs",
                column: "medical_record_id");

            migrationBuilder.CreateIndex(
                name: "idx_link_patient",
                table: "account_patient_profiles",
                columns: new[] { "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_account_patient_profiles_created_by",
                table: "account_patient_profiles",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_account_patient_profiles_verified_by",
                table: "account_patient_profiles",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "uq_link_self_account",
                table: "account_patient_profiles",
                column: "account_id",
                unique: true,
                filter: "relationship = 'SELF' AND status <> 'REVOKED'");

            migrationBuilder.CreateIndex(
                name: "uq_link_self_patient",
                table: "account_patient_profiles",
                column: "patient_id",
                unique: true,
                filter: "relationship = 'SELF' AND status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email",
                table: "accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_role_id",
                table: "accounts",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_username",
                table: "accounts",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_slots_doctor_date",
                table: "appointment_slots",
                columns: new[] { "doctor_id", "start_time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointment_slots_room_id",
                table: "appointment_slots",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_appointment_id",
                table: "appointment_status_history",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_new_status_id",
                table: "appointment_status_history",
                column: "new_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_old_status_id",
                table: "appointment_status_history",
                column: "old_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_performed_by",
                table: "appointment_status_history",
                column: "performed_by");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_statuses_code",
                table: "appointment_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_appointments_booked_by",
                table: "appointments",
                columns: new[] { "booked_by", "status_id" });

            migrationBuilder.CreateIndex(
                name: "idx_appointments_hold",
                table: "appointments",
                column: "hold_until",
                filter: "status_id = 1");

            migrationBuilder.CreateIndex(
                name: "idx_appointments_patient",
                table: "appointments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "idx_appointments_slot",
                table: "appointments",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "idx_appointments_status_id",
                table: "appointments",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_appointment_code",
                table: "appointments",
                column: "appointment_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_department_id",
                table: "appointments",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_doctor_id",
                table: "appointments",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_parent_appointment_id",
                table: "appointments",
                column: "parent_appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_room_id",
                table: "appointments",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "uq_patient_slot_active",
                table: "appointments",
                columns: new[] { "patient_id", "slot_id" },
                unique: true,
                filter: "slot_id IS NOT NULL AND status_id IN (1,2,3,4,5)");

            migrationBuilder.CreateIndex(
                name: "ix_clinic_rooms_department_id",
                table: "clinic_rooms",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_heads_account_id",
                table: "department_heads",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_department_heads_department_id",
                table: "department_heads",
                column: "department_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_schedules_doctor_id",
                table: "doctor_schedules",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_schedules_room_id",
                table: "doctor_schedules",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctors_account_id",
                table: "doctors",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctors_department_id",
                table: "doctors",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "idx_duty_shifts_account",
                table: "duty_shifts",
                columns: new[] { "account_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_duty_shifts_assigned_by",
                table: "duty_shifts",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ix_duty_shifts_department_id",
                table: "duty_shifts",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "idx_insurance_cards_patient",
                table: "insurance_cards",
                columns: new[] { "patient_id", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "ix_insurance_cards_card_number",
                table: "insurance_cards",
                column: "card_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_insurance_cards_insurance_plan_id",
                table: "insurance_cards",
                column: "insurance_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_invoice_id",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_appointment_id",
                table: "invoices",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_insurance_card_id",
                table: "invoices",
                column: "insurance_card_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_insurance_plan_id",
                table: "invoices",
                column: "insurance_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_patient_id",
                table: "invoices",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_orders_medical_record_id",
                table: "lab_orders",
                column: "medical_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_orders_service_id",
                table: "lab_orders",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_results_order_id",
                table: "lab_results",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lab_results_performed_by",
                table: "lab_results",
                column: "performed_by");

            migrationBuilder.CreateIndex(
                name: "idx_records_appointment",
                table: "medical_records",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_doctor_id",
                table: "medical_records",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "idx_notifications_account",
                table: "notifications",
                columns: new[] { "account_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_appointment_id",
                table: "notifications",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_nurses_account_id",
                table: "nurses",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nurses_department_id",
                table: "nurses",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_patients_national_id",
                table: "patients",
                column: "national_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_payments_invoice",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_cashier_id",
                table: "payments",
                column: "cashier_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescription_items_medication_id",
                table: "prescription_items",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescription_items_prescription_id",
                table: "prescription_items",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_medical_record_id",
                table: "prescriptions",
                column: "medical_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_queue_counters_room_id",
                table: "queue_counters",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_exceptions_approved_by",
                table: "schedule_exceptions",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_exceptions_doctor_id",
                table: "schedule_exceptions",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_exceptions_replacement_doctor_id",
                table: "schedule_exceptions",
                column: "replacement_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_types_code",
                table: "service_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_service_type_id",
                table: "services",
                column: "service_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_vital_signs_appointment_id",
                table: "vital_signs",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vital_signs_recorded_by",
                table: "vital_signs",
                column: "recorded_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_logs");

            migrationBuilder.DropTable(
                name: "account_patient_profiles");

            migrationBuilder.DropTable(
                name: "appointment_status_history");

            migrationBuilder.DropTable(
                name: "department_heads");

            migrationBuilder.DropTable(
                name: "doctor_schedules");

            migrationBuilder.DropTable(
                name: "duty_shifts");

            migrationBuilder.DropTable(
                name: "invoice_items");

            migrationBuilder.DropTable(
                name: "lab_results");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "prescription_items");

            migrationBuilder.DropTable(
                name: "queue_counters");

            migrationBuilder.DropTable(
                name: "schedule_exceptions");

            migrationBuilder.DropTable(
                name: "vital_signs");

            migrationBuilder.DropTable(
                name: "lab_orders");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "medications");

            migrationBuilder.DropTable(
                name: "prescriptions");

            migrationBuilder.DropTable(
                name: "nurses");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "insurance_cards");

            migrationBuilder.DropTable(
                name: "medical_records");

            migrationBuilder.DropTable(
                name: "service_types");

            migrationBuilder.DropTable(
                name: "insurance_plans");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "appointment_slots");

            migrationBuilder.DropTable(
                name: "appointment_statuses");

            migrationBuilder.DropTable(
                name: "patients");

            migrationBuilder.DropTable(
                name: "clinic_rooms");

            migrationBuilder.DropTable(
                name: "doctors");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}

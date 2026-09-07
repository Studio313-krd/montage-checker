using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    screenshot_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    screenshot_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    idle_threshold_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.CheckConstraint("ck_employees_idle_threshold", "idle_threshold_seconds >= 0");
                    table.CheckConstraint("ck_employees_screenshot_interval", "screenshot_interval_minutes BETWEEN 1 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "computers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    operating_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    agent_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_online_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_computers", x => x.id);
                    table.ForeignKey(
                        name: "fk_computers_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_screenshot_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_rules_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_refresh_tokens_expiry", "expires_at_utc > created_at_utc");
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_token_id",
                        column: x => x.replaced_by_token_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "render_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    cpu_threshold_percent = table.Column<double>(type: "double precision", nullable: false),
                    disk_write_threshold_bytes_per_second = table.Column<long>(type: "bigint", nullable: false),
                    confirmation_seconds = table.Column<int>(type: "integer", nullable: false),
                    finish_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    minimum_confidence = table.Column<byte>(type: "smallint", nullable: false),
                    file_name_patterns = table.Column<string[]>(type: "text[]", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_render_rules", x => x.id);
                    table.CheckConstraint("ck_render_rules_confidence", "minimum_confidence BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_render_rules_confirmation", "confirmation_seconds > 0");
                    table.CheckConstraint("ck_render_rules_cpu", "cpu_threshold_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_render_rules_disk", "disk_write_threshold_bytes_per_second >= 0");
                    table.CheckConstraint("ck_render_rules_finish", "finish_timeout_seconds > 0");
                    table.ForeignKey(
                        name: "fk_render_rules_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    json_value = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                    table.CheckConstraint("ck_system_settings_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_system_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_employee_access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_employee_access", x => new { x.user_id, x.employee_id });
                    table.ForeignKey(
                        name: "fk_user_employee_access_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_employee_access_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    installed_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    config_version = table.Column<long>(type: "bigint", nullable: false),
                    enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_agents_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    executable_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    window_title = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_sessions", x => x.id);
                    table.CheckConstraint("ck_application_sessions_time", "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_application_sessions_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_application_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "human_state_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_human_state_sessions", x => x.id);
                    table.CheckConstraint("ck_human_state_sessions_time", "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_human_state_sessions_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_human_state_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "machine_state_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    detection_confidence = table.Column<byte>(type: "smallint", nullable: false),
                    detection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_state_sessions", x => x.id);
                    table.CheckConstraint("ck_machine_state_sessions_confidence", "detection_confidence BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_machine_state_sessions_time", "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_machine_state_sessions_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_machine_state_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "render_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    program = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    output_folder = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    output_file = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    average_cpu_percent = table.Column<double>(type: "double precision", nullable: true),
                    max_cpu_percent = table.Column<double>(type: "double precision", nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    detection_confidence = table.Column<byte>(type: "smallint", nullable: false),
                    detection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_render_sessions", x => x.id);
                    table.CheckConstraint("ck_render_sessions_confidence", "detection_confidence BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_render_sessions_cpu", "(average_cpu_percent IS NULL OR average_cpu_percent BETWEEN 0 AND 100) AND (max_cpu_percent IS NULL OR max_cpu_percent BETWEEN 0 AND 100)");
                    table.CheckConstraint("ck_render_sessions_file_size", "file_size_bytes IS NULL OR file_size_bytes >= 0");
                    table.CheckConstraint("ck_render_sessions_time", "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_render_sessions_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_render_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "screenshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    screen_index = table.Column<int>(type: "integer", nullable: false),
                    foreground_process = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    foreground_window_title = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    human_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    machine_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    file_deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_screenshots", x => x.id);
                    table.CheckConstraint("ck_screenshots_dimensions", "width > 0 AND height > 0");
                    table.CheckConstraint("ck_screenshots_file_size", "file_size_bytes >= 0");
                    table.CheckConstraint("ck_screenshots_screen_index", "screen_index >= 0");
                    table.ForeignKey(
                        name: "fk_screenshots_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_screenshots_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "watched_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    path_pattern = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    extensions = table.Column<string[]>(type: "text[]", nullable: false),
                    file_name_patterns = table.Column<string[]>(type: "text[]", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watched_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_watched_folders_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_events", x => x.event_id);
                    table.CheckConstraint("ck_activity_events_schema_version", "schema_version > 0");
                    table.ForeignKey(
                        name: "fk_activity_events_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_events_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_events_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_credentials_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "heartbeats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    human_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    machine_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    foreground_process = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    foreground_window_title = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    idle_seconds = table.Column<int>(type: "integer", nullable: false),
                    cpu_load_percent = table.Column<double>(type: "double precision", nullable: false),
                    memory_load_percent = table.Column<double>(type: "double precision", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_heartbeats", x => x.id);
                    table.CheckConstraint("ck_heartbeats_cpu", "cpu_load_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_heartbeats_idle", "idle_seconds >= 0");
                    table.CheckConstraint("ck_heartbeats_memory", "memory_load_percent BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_heartbeats_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_heartbeats_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_heartbeats_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "application_rules",
                columns: new[] { "id", "classification", "created_at_utc", "display_name", "is_enabled", "is_screenshot_excluded", "normalized_process_name", "priority", "process_name", "updated_at_utc", "updated_by_user_id" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Adobe Premiere Pro", true, false, "ADOBE PREMIERE PRO.EXE", 0, "Adobe Premiere Pro.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Adobe Media Encoder", true, false, "ADOBE MEDIA ENCODER.EXE", 0, "Adobe Media Encoder.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Adobe After Effects", true, false, "AFTERFX.EXE", 0, "AfterFX.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "After Effects Render Engine", true, false, "AERENDER.EXE", 0, "aerender.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "DaVinci Resolve", true, false, "RESOLVE.EXE", 0, "Resolve.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000006"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FFmpeg", true, false, "FFMPEG.EXE", 0, "ffmpeg.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000007"), "Productive", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Blender", true, false, "BLENDER.EXE", 0, "Blender.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000008"), "Neutral", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Google Chrome", true, false, "CHROME.EXE", 0, "chrome.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000009"), "Ignored", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "1Password", true, true, "1PASSWORD.EXE", 0, "1Password.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("10000000-0000-0000-0000-000000000010"), "Ignored", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "KeePass", true, true, "KEEPASS.EXE", 0, "KeePass.exe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                table: "system_settings",
                columns: new[] { "key", "json_value", "updated_at_utc", "updated_by_user_id", "version" },
                values: new object[,]
                {
                    { "activity.idleThresholdSeconds", "300", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "agent.heartbeatIntervalSeconds", "30", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "agent.latestVersion", "\"0.1.0\"", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "agent.syncIntervalSeconds", "45", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "backup.retentionDays", "14", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "company.timeZone", "\"Europe/Moscow\"", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "screenshots.intervalMinutes", "5", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "screenshots.retentionDays", "30", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "telemetry.retentionDays", "365", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_agent_id_received_at_utc",
                table: "activity_events",
                columns: new[] { "agent_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_computer_id_occurred_at_utc",
                table: "activity_events",
                columns: new[] { "computer_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_employee_id_occurred_at_utc",
                table: "activity_events",
                columns: new[] { "employee_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_credentials_agent_id_revoked_at_utc",
                table: "agent_credentials",
                columns: new[] { "agent_id", "revoked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agents_computer_id",
                table: "agents",
                column: "computer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agents_status_last_seen_at_utc",
                table: "agents",
                columns: new[] { "status", "last_seen_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_application_rules_is_enabled_priority",
                table: "application_rules",
                columns: new[] { "is_enabled", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_application_rules_normalized_process_name",
                table: "application_rules",
                column: "normalized_process_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_rules_updated_by_user_id",
                table: "application_rules",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_sessions_computer_id_started_at_utc",
                table: "application_sessions",
                columns: new[] { "computer_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_application_sessions_employee_id_process_name_started_at_utc",
                table: "application_sessions",
                columns: new[] { "employee_id", "process_name", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_application_sessions_employee_id_started_at_utc",
                table: "application_sessions",
                columns: new[] { "employee_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp_utc",
                table: "audit_logs",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_timestamp_utc",
                table: "audit_logs",
                columns: new[] { "user_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_computers_employee_id_name",
                table: "computers",
                columns: new[] { "employee_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_computers_last_heartbeat_at_utc",
                table: "computers",
                column: "last_heartbeat_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_employees_is_active_name",
                table: "employees",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_normalized_login",
                table: "employees",
                column: "normalized_login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_heartbeats_agent_id",
                table: "heartbeats",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_heartbeats_computer_id_timestamp_utc",
                table: "heartbeats",
                columns: new[] { "computer_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_heartbeats_employee_id_timestamp_utc",
                table: "heartbeats",
                columns: new[] { "employee_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_heartbeats_event_id",
                table: "heartbeats",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_human_state_sessions_computer_id_started_at_utc",
                table: "human_state_sessions",
                columns: new[] { "computer_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_human_state_sessions_employee_id_started_at_utc",
                table: "human_state_sessions",
                columns: new[] { "employee_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_human_state_sessions_employee_id_state_started_at_utc",
                table: "human_state_sessions",
                columns: new[] { "employee_id", "state", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_state_sessions_computer_id_started_at_utc",
                table: "machine_state_sessions",
                columns: new[] { "computer_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_state_sessions_employee_id_started_at_utc",
                table: "machine_state_sessions",
                columns: new[] { "employee_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_state_sessions_employee_id_state_started_at_utc",
                table: "machine_state_sessions",
                columns: new[] { "employee_id", "state", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_token_id",
                table: "refresh_tokens",
                column: "replaced_by_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at_utc",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_render_rules_is_enabled_type_process_name",
                table: "render_rules",
                columns: new[] { "is_enabled", "type", "process_name" });

            migrationBuilder.CreateIndex(
                name: "ix_render_rules_updated_by_user_id",
                table: "render_rules",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_render_sessions_computer_id_started_at_utc",
                table: "render_sessions",
                columns: new[] { "computer_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_render_sessions_employee_id_started_at_utc",
                table: "render_sessions",
                columns: new[] { "employee_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_render_sessions_type_started_at_utc",
                table: "render_sessions",
                columns: new[] { "type", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_screenshots_computer_id_timestamp_utc",
                table: "screenshots",
                columns: new[] { "computer_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_screenshots_employee_id_timestamp_utc",
                table: "screenshots",
                columns: new[] { "employee_id", "timestamp_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_screenshots_storage_path",
                table: "screenshots",
                column: "storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_screenshots_timestamp_utc_file_deleted_at_utc",
                table: "screenshots",
                columns: new[] { "timestamp_utc", "file_deleted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_updated_by_user_id",
                table: "system_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_employee_access_employee_id",
                table: "user_employee_access",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_login",
                table: "users",
                column: "normalized_login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_watched_folders_computer_id_type_is_enabled",
                table: "watched_folders",
                columns: new[] { "computer_id", "type", "is_enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropTable(
                name: "agent_credentials");

            migrationBuilder.DropTable(
                name: "application_rules");

            migrationBuilder.DropTable(
                name: "application_sessions");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "heartbeats");

            migrationBuilder.DropTable(
                name: "human_state_sessions");

            migrationBuilder.DropTable(
                name: "machine_state_sessions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "render_rules");

            migrationBuilder.DropTable(
                name: "render_sessions");

            migrationBuilder.DropTable(
                name: "screenshots");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "user_employee_access");

            migrationBuilder.DropTable(
                name: "watched_folders");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "computers");

            migrationBuilder.DropTable(
                name: "employees");
        }
    }
}

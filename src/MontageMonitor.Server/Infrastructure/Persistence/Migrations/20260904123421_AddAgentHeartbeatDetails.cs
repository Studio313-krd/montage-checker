using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHeartbeatDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_computers_employee_id_name",
                table: "computers");

            migrationBuilder.AddColumn<string>(
                name: "agent_version",
                table: "heartbeats",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "foreground_executable_path",
                table: "heartbeats",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "machine_name",
                table: "heartbeats",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "windows_user",
                table: "heartbeats",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "computers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE heartbeats
                SET agent_version = 'unknown', windows_user = 'unknown', machine_name = 'unknown'
                WHERE agent_version = '' OR windows_user = '' OR machine_name = '';

                WITH ranked AS (
                    SELECT id, name, ROW_NUMBER() OVER (
                        PARTITION BY employee_id, UPPER(name) ORDER BY id) AS row_number
                    FROM computers
                )
                UPDATE computers AS computer
                SET normalized_name = CASE
                    WHEN ranked.row_number = 1 THEN UPPER(ranked.name)
                    ELSE LEFT(UPPER(ranked.name), 218) || '#' || REPLACE(ranked.id::text, '-', '')
                END
                FROM ranked
                WHERE computer.id = ranked.id;

                ALTER TABLE heartbeats ALTER COLUMN agent_version DROP DEFAULT;
                ALTER TABLE heartbeats ALTER COLUMN windows_user DROP DEFAULT;
                ALTER TABLE heartbeats ALTER COLUMN machine_name DROP DEFAULT;
                ALTER TABLE computers ALTER COLUMN normalized_name DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_heartbeats_identity_text",
                table: "heartbeats",
                sql: "agent_version <> '' AND windows_user <> '' AND machine_name <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_computers_employee_id_normalized_name",
                table: "computers",
                columns: new[] { "employee_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_computers_normalized_name",
                table: "computers",
                sql: "normalized_name <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_heartbeats_identity_text",
                table: "heartbeats");

            migrationBuilder.DropIndex(
                name: "ix_computers_employee_id_normalized_name",
                table: "computers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_computers_normalized_name",
                table: "computers");

            migrationBuilder.DropColumn(
                name: "agent_version",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "foreground_executable_path",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "machine_name",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "windows_user",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "computers");

            migrationBuilder.CreateIndex(
                name: "ix_computers_employee_id_name",
                table: "computers",
                columns: new[] { "employee_id", "name" },
                unique: true);
        }
    }
}

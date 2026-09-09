using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyOperatorSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operator_pin_protected",
                table: "employees",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_operator_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_operator_sessions", x => x.id);
                    table.CheckConstraint("ck_agent_operator_sessions_time", "expires_at_utc > started_at_utc AND (ended_at_utc IS NULL OR ended_at_utc >= started_at_utc)");
                    table.ForeignKey(
                        name: "fk_agent_operator_sessions_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_operator_sessions_computers_computer_id",
                        column: x => x.computer_id,
                        principalTable: "computers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_operator_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operator_sessions_agent_id",
                table: "agent_operator_sessions",
                column: "agent_id",
                unique: true,
                filter: "ended_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_operator_sessions_agent_id_started_at_utc",
                table: "agent_operator_sessions",
                columns: new[] { "agent_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operator_sessions_computer_id_started_at_utc",
                table: "agent_operator_sessions",
                columns: new[] { "computer_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_operator_sessions_employee_id_started_at_utc",
                table: "agent_operator_sessions",
                columns: new[] { "employee_id", "started_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_operator_sessions");

            migrationBuilder.DropColumn(
                name: "operator_pin_protected",
                table: "employees");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityAggregationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_machine_state_sessions_computer_id",
                table: "machine_state_sessions",
                column: "computer_id",
                unique: true,
                filter: "ended_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_human_state_sessions_computer_id",
                table: "human_state_sessions",
                column: "computer_id",
                unique: true,
                filter: "ended_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_application_sessions_computer_id",
                table: "application_sessions",
                column: "computer_id",
                unique: true,
                filter: "ended_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_machine_state_sessions_computer_id",
                table: "machine_state_sessions");

            migrationBuilder.DropIndex(
                name: "ix_human_state_sessions_computer_id",
                table: "human_state_sessions");

            migrationBuilder.DropIndex(
                name: "ix_application_sessions_computer_id",
                table: "application_sessions");
        }
    }
}

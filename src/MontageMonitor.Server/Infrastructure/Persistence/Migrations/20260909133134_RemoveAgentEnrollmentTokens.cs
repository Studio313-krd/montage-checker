using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAgentEnrollmentTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_enrollment_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_enrollment_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumed_by_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_enrollment_tokens", x => x.id);
                    table.CheckConstraint("ck_agent_enrollment_tokens_expiry", "expires_at_utc > created_at_utc");
                    table.ForeignKey(
                        name: "fk_agent_enrollment_tokens_agents_consumed_by_agent_id",
                        column: x => x.consumed_by_agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_agent_enrollment_tokens_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_agent_enrollment_tokens_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_tokens_consumed_by_agent_id",
                table: "agent_enrollment_tokens",
                column: "consumed_by_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_tokens_created_by_user_id",
                table: "agent_enrollment_tokens",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_tokens_employee_id_expires_at_utc_used_at_",
                table: "agent_enrollment_tokens",
                columns: new[] { "employee_id", "expires_at_utc", "used_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_tokens_token_hash",
                table: "agent_enrollment_tokens",
                column: "token_hash",
                unique: true);
        }
    }
}

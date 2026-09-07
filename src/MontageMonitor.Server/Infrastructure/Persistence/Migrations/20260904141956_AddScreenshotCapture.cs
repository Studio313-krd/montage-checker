using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenshotCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "screenshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE screenshots
                SET event_id = md5(id::text)::uuid
                WHERE event_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "screenshots",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "system_settings",
                columns: new[] { "key", "json_value", "updated_at_utc", "updated_by_user_id", "version" },
                values: new object[,]
                {
                    { "screenshots.captureMode", "\"PrimaryMonitor\"", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "screenshots.jpegQuality", "60", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L },
                    { "screenshots.maxWidth", "1600", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "ix_screenshots_event_id",
                table: "screenshots",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_screenshots_event_id",
                table: "screenshots");

            migrationBuilder.DeleteData(
                table: "system_settings",
                keyColumn: "key",
                keyValue: "screenshots.captureMode");

            migrationBuilder.DeleteData(
                table: "system_settings",
                keyColumn: "key",
                keyValue: "screenshots.jpegQuality");

            migrationBuilder.DeleteData(
                table: "system_settings",
                keyColumn: "key",
                keyValue: "screenshots.maxWidth");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "screenshots");
        }
    }
}

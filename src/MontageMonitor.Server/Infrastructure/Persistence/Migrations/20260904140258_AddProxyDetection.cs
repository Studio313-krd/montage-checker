using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "render_rules",
                columns: new[] { "id", "confirmation_seconds", "cpu_threshold_percent", "created_at_utc", "disk_write_threshold_bytes_per_second", "file_name_patterns", "finish_timeout_seconds", "is_enabled", "minimum_confidence", "name", "process_name", "type", "updated_at_utc", "updated_by_user_id" },
                values: new object[,]
                {
                    { new Guid("21000000-0000-0000-0000-000000000001"), 15, 20.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new[] { "*_Proxy.mov", "*_Proxy.mp4", "*proxy*.mxf" }, 30, true, (byte)70, "Adobe Media Encoder — Proxy", "Adobe Media Encoder.exe", "Proxy", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("21000000-0000-0000-0000-000000000002"), 15, 25.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new[] { "*_Proxy.mov", "*_Proxy.mp4", "*proxy*.mxf" }, 30, true, (byte)70, "DaVinci Resolve — Proxy", "Resolve.exe", "Proxy", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("21000000-0000-0000-0000-000000000003"), 15, 25.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new[] { "*_Proxy.mov", "*_Proxy.mp4", "*proxy*.mxf" }, 30, true, (byte)70, "FFmpeg — Proxy", "ffmpeg.exe", "Proxy", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("21000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("21000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("21000000-0000-0000-0000-000000000003"));
        }
    }
}

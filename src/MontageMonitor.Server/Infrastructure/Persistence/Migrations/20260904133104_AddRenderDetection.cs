using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MontageMonitor.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRenderDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "render_child_process_count",
                table: "heartbeats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "render_detection_confidence",
                table: "heartbeats",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_detection_reason",
                table: "heartbeats",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "render_gpu_load_percent",
                table: "heartbeats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_output_file",
                table: "heartbeats",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "render_output_file_size_bytes",
                table: "heartbeats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_output_folder",
                table: "heartbeats",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "render_process_cpu_percent",
                table: "heartbeats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "render_process_io_read_bytes_per_second",
                table: "heartbeats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "render_process_io_write_bytes_per_second",
                table: "heartbeats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "render_process_started_at_utc",
                table: "heartbeats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "render_process_working_set_bytes",
                table: "heartbeats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "render_program",
                table: "heartbeats",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.InsertData(
                table: "render_rules",
                columns: new[] { "id", "confirmation_seconds", "cpu_threshold_percent", "created_at_utc", "disk_write_threshold_bytes_per_second", "file_name_patterns", "finish_timeout_seconds", "is_enabled", "minimum_confidence", "name", "process_name", "type", "updated_at_utc", "updated_by_user_id" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), 15, 35.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "Adobe Premiere Pro", "Adobe Premiere Pro.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000002"), 15, 20.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "Adobe Media Encoder", "Adobe Media Encoder.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000003"), 15, 40.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "Adobe After Effects", "AfterFX.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000004"), 15, 30.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "After Effects Render Engine", "aerender.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000005"), 15, 25.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "DaVinci Resolve", "Resolve.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000006"), 15, 25.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "FFmpeg", "ffmpeg.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("20000000-0000-0000-0000-000000000007"), 15, 40.0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1048576L, new string[0], 30, true, (byte)70, "Blender", "Blender.exe", "Render", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_render_sessions_computer_id",
                table: "render_sessions",
                column: "computer_id",
                unique: true,
                filter: "ended_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_heartbeats_computer_id_machine_state_timestamp_utc",
                table: "heartbeats",
                columns: new[] { "computer_id", "machine_state", "timestamp_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_heartbeats_render_metrics",
                table: "heartbeats",
                sql: "(render_process_cpu_percent IS NULL OR render_process_cpu_percent BETWEEN 0 AND 100) AND (render_gpu_load_percent IS NULL OR render_gpu_load_percent BETWEEN 0 AND 100) AND (render_process_working_set_bytes IS NULL OR render_process_working_set_bytes >= 0) AND (render_process_io_read_bytes_per_second IS NULL OR render_process_io_read_bytes_per_second >= 0) AND (render_process_io_write_bytes_per_second IS NULL OR render_process_io_write_bytes_per_second >= 0) AND (render_child_process_count IS NULL OR render_child_process_count >= 0) AND (render_output_file_size_bytes IS NULL OR render_output_file_size_bytes >= 0) AND (render_detection_confidence IS NULL OR render_detection_confidence BETWEEN 0 AND 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_render_sessions_computer_id",
                table: "render_sessions");

            migrationBuilder.DropIndex(
                name: "ix_heartbeats_computer_id_machine_state_timestamp_utc",
                table: "heartbeats");

            migrationBuilder.DropCheckConstraint(
                name: "ck_heartbeats_render_metrics",
                table: "heartbeats");

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "render_rules",
                keyColumn: "id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000007"));

            migrationBuilder.DropColumn(
                name: "render_child_process_count",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_detection_confidence",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_detection_reason",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_gpu_load_percent",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_output_file",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_output_file_size_bytes",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_output_folder",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_process_cpu_percent",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_process_io_read_bytes_per_second",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_process_io_write_bytes_per_second",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_process_started_at_utc",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_process_working_set_bytes",
                table: "heartbeats");

            migrationBuilder.DropColumn(
                name: "render_program",
                table: "heartbeats");
        }
    }
}

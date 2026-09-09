using ClosedXML.Excel;
using MontageMonitor.Server.Domain;
using MontageMonitor.Server.Features.Reports;
using MontageMonitor.Shared.States;
using Xunit;

namespace MontageMonitor.Agent.Tests;

public sealed class ExcelReportWorkbookWriterTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_CreatesTypedAndFormattedFiveSheetWorkbook()
    {
        var employeeId = Guid.NewGuid();
        var computerId = Guid.NewGuid();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var range = new ReportRange(Start, Start.AddDays(1), Start.AddDays(1), Start.AddDays(1), timeZone);
        var data = new ExcelReportData(
            range,
            [new ExcelSummaryRow(
                employeeId, "Иван Петров", new DateOnly(2026, 9, 7), Start, Start.AddHours(8),
                28_800, 25_200, 21_600, 3_600, 0, 0, 1_800, 900, 0, 1, 0, 12)],
            [new ExcelTimelineRow(
                employeeId, "Иван Петров", computerId, "EDIT-01", Start, Start.AddHours(1),
                HumanState.Active, MachineState.Normal, "Adobe Premiere Pro", "Монтаж фильма")],
            [new ExcelApplicationRow(
                employeeId, "Иван Петров", computerId, "EDIT-01", new DateOnly(2026, 9, 7), "Adobe Premiere Pro",
                ApplicationClassification.Productive, 21_600)],
            [new ExcelRenderRow(
                employeeId, "Иван Петров", computerId, "EDIT-01", ProcessingType.Render, "Adobe Media Encoder", Start.AddHours(4),
                Start.AddHours(5), @"D:\render\film.mp4", 94, "Высокая загрузка CPU")],
            [new ExcelIdleRow(employeeId, "Иван Петров", computerId, "EDIT-01", Start.AddHours(6), Start.AddHours(6.5))]);

        var bytes = new ExcelReportWorkbookWriter().Write(data);

        Assert.True(bytes.Length > 1_000);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(
            ["SUMMARY", "TIMELINE", "APPLICATIONS", "RENDERS", "IDLE"],
            workbook.Worksheets.Select(sheet => sheet.Name));

        var summary = workbook.Worksheet("SUMMARY");
        Assert.Equal("Сотрудник", summary.Cell("A1").GetString());
        Assert.Equal("Количество снимков", summary.Cell("P1").GetString());
        Assert.Equal(XLDataType.DateTime, summary.Cell("B2").DataType);
        Assert.Equal(XLDataType.TimeSpan, summary.Cell("E2").DataType);
        Assert.Equal("[h]:mm:ss", summary.Cell("E2").Style.NumberFormat.Format);
        Assert.Equal(TimeSpan.FromHours(8), summary.Cell("E2").GetTimeSpan());
        Assert.Equal(12, summary.Cell("P2").GetValue<int>());
        Assert.True(summary.AutoFilter.IsEnabled);
        Assert.Equal(1, summary.SheetView.SplitRow);
        Assert.True(summary.Cell("A1").Style.Font.Bold);

        var timeline = workbook.Worksheet("TIMELINE");
        Assert.Equal(XLDataType.DateTime, timeline.Cell("C2").DataType);
        Assert.Equal("Активная работа", timeline.Cell("F2").GetString());
        Assert.Equal("Обычная работа", timeline.Cell("G2").GetString());

        var renders = workbook.Worksheet("RENDERS");
        Assert.Equal("Рендер", renders.Cell("C2").GetString());
        Assert.Equal(0.94, renders.Cell("I2").GetDouble(), 8);
        Assert.Equal("0%", renders.Cell("I2").Style.NumberFormat.Format);
    }

    [Fact]
    public void Write_ProducesFilterableEmptySheets()
    {
        var range = new ReportRange(Start, Start.AddHours(1), Start.AddHours(1), Start, TimeZoneInfo.Utc);
        var data = new ExcelReportData(range, [], [], [], [], []);

        using var stream = new MemoryStream(new ExcelReportWorkbookWriter().Write(data));
        using var workbook = new XLWorkbook(stream);

        Assert.All(workbook.Worksheets, sheet =>
        {
            Assert.True(sheet.AutoFilter.IsEnabled);
            Assert.NotEmpty(sheet.Cell("A1").GetString());
        });
    }
}

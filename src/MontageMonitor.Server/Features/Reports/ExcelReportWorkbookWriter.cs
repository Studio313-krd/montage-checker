using ClosedXML.Excel;
using MontageMonitor.Server.Domain;
using MontageMonitor.Shared.States;

namespace MontageMonitor.Server.Features.Reports;

internal sealed class ExcelReportWorkbookWriter
{
    private const string DateFormat = "dd.mm.yyyy";
    private const string DateTimeFormat = "dd.mm.yyyy hh:mm:ss";
    private const string DurationFormat = "[h]:mm:ss";

    public byte[] Write(ExcelReportData data)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Отчёт MontageMonitor";
        workbook.Properties.Subject = "Рабочее время сотрудников";
        workbook.Properties.Company = "MontageMonitor";
        workbook.Properties.Created = data.Range.NowUtc.UtcDateTime;

        WriteSummary(workbook.Worksheets.Add("SUMMARY"), data);
        WriteTimeline(workbook.Worksheets.Add("TIMELINE"), data);
        WriteApplications(workbook.Worksheets.Add("APPLICATIONS"), data);
        WriteRenders(workbook.Worksheets.Add("RENDERS"), data);
        WriteIdle(workbook.Worksheets.Add("IDLE"), data);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSummary(IXLWorksheet sheet, ExcelReportData data)
    {
        string[] headers =
        [
            "Сотрудник", "Дата", "Первое событие", "Последнее событие", "Человеко-время без пересечений",
            "Продуктивное время без пересечений", "Активная работа без пересечений",
            "Рендер, машино-время", "Прокси, машино-время", "Фоновая обработка, машино-время",
            "Простой без пересечений", "Locked без пересечений", "Offline без пересечений",
            "Использовано компьютеров", "Параллельная работа", "Количество снимков",
        ];
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var item in data.Summary)
        {
            sheet.Cell(row, 1).Value = item.EmployeeName;
            WriteDate(sheet.Cell(row, 2), item.Date);
            WriteDateTime(sheet.Cell(row, 3), item.FirstEventAtUtc, data.Range.TimeZone);
            WriteDateTime(sheet.Cell(row, 4), item.LastEventAtUtc, data.Range.TimeZone);
            WriteDuration(sheet.Cell(row, 5), item.TotalSeconds);
            WriteDuration(sheet.Cell(row, 6), item.ProductiveSeconds);
            WriteDuration(sheet.Cell(row, 7), item.ActiveSeconds);
            WriteDuration(sheet.Cell(row, 8), item.RenderSeconds);
            WriteDuration(sheet.Cell(row, 9), item.ProxySeconds);
            WriteDuration(sheet.Cell(row, 10), item.BackgroundSeconds);
            WriteDuration(sheet.Cell(row, 11), item.IdleSeconds);
            WriteDuration(sheet.Cell(row, 12), item.LockedSeconds);
            WriteDuration(sheet.Cell(row, 13), item.OfflineSeconds);
            sheet.Cell(row, 14).Value = item.ComputerCount;
            WriteDuration(sheet.Cell(row, 15), item.ParallelSeconds);
            sheet.Cell(row, 16).Value = item.ScreenshotCount;
            row++;
        }

        FinishSheet(sheet, headers.Length, row - 1, [28, 13, 21, 21, 28, 30, 28, 24, 24, 31, 24, 24, 24, 24, 22, 20]);
    }

    private static void WriteTimeline(IXLWorksheet sheet, ExcelReportData data)
    {
        string[] headers =
        [
            "Сотрудник", "Компьютер", "Начало", "Конец", "Длительность", "Состояние человека",
            "Состояние компьютера", "Приложение", "Заголовок окна",
        ];
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var item in data.Timeline)
        {
            sheet.Cell(row, 1).Value = item.EmployeeName;
            sheet.Cell(row, 2).Value = item.ComputerName;
            WriteDateTime(sheet.Cell(row, 3), item.StartedAtUtc, data.Range.TimeZone);
            WriteDateTime(sheet.Cell(row, 4), item.EndedAtUtc, data.Range.TimeZone);
            WriteDuration(sheet.Cell(row, 5), item.DurationSeconds);
            sheet.Cell(row, 6).Value = HumanStateLabel(item.HumanState);
            sheet.Cell(row, 7).Value = MachineStateLabel(item.MachineState);
            sheet.Cell(row, 8).Value = item.Application ?? string.Empty;
            sheet.Cell(row, 9).Value = item.WindowTitle ?? string.Empty;
            row++;
        }

        FinishSheet(sheet, headers.Length, row - 1, [28, 24, 21, 21, 17, 22, 24, 28, 54]);
    }

    private static void WriteApplications(IXLWorksheet sheet, ExcelReportData data)
    {
        string[] headers = ["Сотрудник", "Компьютер", "Приложение", "Длительность на ПК", "Классификация", "Дата"];
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var item in data.Applications)
        {
            sheet.Cell(row, 1).Value = item.EmployeeName;
            sheet.Cell(row, 2).Value = item.ComputerName;
            sheet.Cell(row, 3).Value = item.Application;
            WriteDuration(sheet.Cell(row, 4), item.DurationSeconds);
            sheet.Cell(row, 5).Value = ClassificationLabel(item.Classification);
            WriteDate(sheet.Cell(row, 6), item.Date);
            row++;
        }

        FinishSheet(sheet, headers.Length, row - 1, [28, 24, 32, 21, 22, 13]);
    }

    private static void WriteRenders(IXLWorksheet sheet, ExcelReportData data)
    {
        string[] headers =
        [
            "Сотрудник", "Компьютер", "Тип", "Приложение", "Начало", "Конец", "Длительность на ПК", "Результат",
            "Уверенность определения", "Причина определения",
        ];
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var item in data.Renders)
        {
            sheet.Cell(row, 1).Value = item.EmployeeName;
            sheet.Cell(row, 2).Value = item.ComputerName;
            sheet.Cell(row, 3).Value = ProcessingTypeLabel(item.Type);
            sheet.Cell(row, 4).Value = item.Application;
            WriteDateTime(sheet.Cell(row, 5), item.StartedAtUtc, data.Range.TimeZone);
            WriteDateTime(sheet.Cell(row, 6), item.EndedAtUtc, data.Range.TimeZone);
            WriteDuration(sheet.Cell(row, 7), item.DurationSeconds);
            sheet.Cell(row, 8).Value = item.Output ?? string.Empty;
            sheet.Cell(row, 9).Value = item.DetectionConfidence / 100d;
            sheet.Cell(row, 9).Style.NumberFormat.Format = "0%";
            sheet.Cell(row, 10).Value = item.DetectionReason;
            row++;
        }

        FinishSheet(sheet, headers.Length, row - 1, [28, 24, 20, 28, 21, 21, 21, 54, 25, 54]);
    }

    private static void WriteIdle(IXLWorksheet sheet, ExcelReportData data)
    {
        string[] headers = ["Сотрудник", "Компьютер", "Начало", "Конец", "Длительность на ПК"];
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var item in data.Idle)
        {
            sheet.Cell(row, 1).Value = item.EmployeeName;
            sheet.Cell(row, 2).Value = item.ComputerName;
            WriteDateTime(sheet.Cell(row, 3), item.StartedAtUtc, data.Range.TimeZone);
            WriteDateTime(sheet.Cell(row, 4), item.EndedAtUtc, data.Range.TimeZone);
            WriteDuration(sheet.Cell(row, 5), item.DurationSeconds);
            row++;
        }

        FinishSheet(sheet, headers.Length, row - 1, [28, 24, 21, 21, 21]);
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
        {
            sheet.Cell(1, column).Value = headers[column - 1];
        }

        var header = sheet.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#19323C");
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        sheet.Row(1).Height = 30;
    }

    private static void FinishSheet(
        IXLWorksheet sheet,
        int columnCount,
        int lastRow,
        IReadOnlyList<double> widths)
    {
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(lastRow, 1), columnCount).SetAutoFilter();
        sheet.Range(1, 1, Math.Max(lastRow, 1), columnCount).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Top;
        for (var column = 1; column <= columnCount; column++)
        {
            sheet.Column(column).Width = widths[column - 1];
        }

        if (lastRow >= 2)
        {
            sheet.Range(2, 1, lastRow, columnCount).Style.Alignment.WrapText = true;
        }

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
    }

    private static void WriteDate(IXLCell cell, DateOnly value)
    {
        cell.Value = value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = DateFormat;
    }

    private static void WriteDateTime(IXLCell cell, DateTimeOffset value, TimeZoneInfo timeZone)
    {
        cell.Value = TimeZoneInfo.ConvertTime(value, timeZone).DateTime;
        cell.Style.NumberFormat.Format = DateTimeFormat;
    }

    private static void WriteDateTime(IXLCell cell, DateTimeOffset? value, TimeZoneInfo timeZone)
    {
        if (value.HasValue)
        {
            WriteDateTime(cell, value.Value, timeZone);
        }
    }

    private static void WriteDuration(IXLCell cell, double seconds)
    {
        cell.Value = Math.Max(0, seconds) / TimeSpan.FromDays(1).TotalSeconds;
        cell.Style.NumberFormat.Format = DurationFormat;
    }

    private static string HumanStateLabel(HumanState? state) => state switch
    {
        HumanState.Active => "Активная работа",
        HumanState.Idle => "Простой",
        HumanState.Locked => "Заблокирован",
        HumanState.Offline => "Не в сети",
        _ => string.Empty,
    };

    private static string MachineStateLabel(MachineState? state) => state switch
    {
        MachineState.Normal => "Обычная работа",
        MachineState.Render => "Рендер",
        MachineState.Proxy => "Прокси",
        MachineState.BackgroundProcessing => "Фоновая обработка",
        _ => string.Empty,
    };

    private static string ClassificationLabel(ApplicationClassification classification) => classification switch
    {
        ApplicationClassification.Productive => "Продуктивное",
        ApplicationClassification.Neutral => "Нейтральное",
        ApplicationClassification.Unproductive => "Непродуктивное",
        ApplicationClassification.Ignored => "Игнорируется",
        _ => "Не классифицировано",
    };

    private static string ProcessingTypeLabel(ProcessingType type) => type switch
    {
        ProcessingType.Render => "Рендер",
        ProcessingType.Proxy => "Прокси",
        ProcessingType.Background => "Фоновая обработка",
        _ => type.ToString(),
    };
}

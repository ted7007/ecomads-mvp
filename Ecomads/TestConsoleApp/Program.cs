using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

var filePath = "C:\\Work\\ecomads-mvp\\Ecomads\\TestConsoleApp\\wb-general-stat-for-period.xlsx";

var xlsxPath = filePath;
var sheetName = args.Length >= 2 ? args[1] : "Статистика";

try
{
    using var doc = SpreadsheetDocument.Open(xlsxPath, false);
    var wbPart = doc.WorkbookPart!;
    var sstPart = wbPart.SharedStringTablePart;

    var wsPart = GetWorksheetPartByName(wbPart, sheetName);
    if (wsPart == null)
    {
        Console.WriteLine($"Sheet not found: {sheetName}");
        return 3;
    }

    var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
    if (sheetData == null)
    {
        Console.WriteLine("Sheet has no data.");
        return 4;
    }

    // Преобразуем строки листа в список: row -> (colIndex -> value)
    var rows = new List<Dictionary<int, string?>>();
    int maxCol = 0;

    foreach (var row in sheetData.Elements<Row>())
    {
        var dict = new Dictionary<int, string?>();
        foreach (var cell in row.Elements<Cell>())
        {
            int colIdx = GetColumnIndex(cell.CellReference?.Value);
            string? val = GetCellValue(cell, sstPart);
            dict[colIdx] = val;
            if (colIdx > maxCol) maxCol = colIdx;
        }

        // Пропускаем полностью пустые строки
        if (dict.Count > 0 && dict.Values.Any(v => !string.IsNullOrEmpty(v)))
            rows.Add(dict);
    }

    if (rows.Count == 0)
    {
        Console.WriteLine("Sheet is empty.");
        return 5;
    }

    // Заголовок — первая непустая строка
    var headerRow = rows[0];
    var headerIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var kvp in headerRow)
    {
        var header = kvp.Value?.Trim();
        if (!string.IsNullOrEmpty(header) && !headerIndexByName.ContainsKey(header))
            headerIndexByName[header] = kvp.Key;
    }

    // Проверяем нужные колонки
    string[] required =
    {
        "Название",
        "Затраты, RUB",
        "Заказов на сумму, RUB",
        "Клики",
        "CTR(%)"
    };

    foreach (var col in required)
    {
        if (!headerIndexByName.ContainsKey(col))
        {
            Console.WriteLine($"Required column not found: \"{col}\"");
            return 6;
        }
    }

    int idxTitle = headerIndexByName["Название"];
    int idxSpend = headerIndexByName["Затраты, RUB"];
    int idxRevenue = headerIndexByName["Заказов на сумму, RUB"];
    int idxClicks = headerIndexByName["Клики"];
    int idxCtr = headerIndexByName["CTR(%)"];

    // Ищем строку "Всего по кампании"
    Dictionary<int, string?>? totalRow = null;
    foreach (var r in rows.Skip(1))
    {
        var title = r.TryGetValue(idxTitle, out var t) ? t : null;
        if (!string.IsNullOrWhiteSpace(title) &&
            title.Trim().Equals("Всего по кампании", StringComparison.OrdinalIgnoreCase))
        {
            totalRow = r;
            break;
        }
    }

    if (totalRow == null)
    {
        Console.WriteLine("Row \"Всего по кампании\" not found.");
        return 7;
    }

    // Парсим значения
    decimal spend = ParseDecimal(Get(totalRow, idxSpend));
    decimal revenue = ParseDecimal(Get(totalRow, idxRevenue));
    long clicks = ParseLong(Get(totalRow, idxClicks));
    double ctrPct = ParseDouble(Get(totalRow, idxCtr));

    double drrPct = revenue > 0m ? (double)(spend / revenue) * 100.0 : 0.0;

    // Печать таблички
    PrintTable(spend, drrPct, clicks, ctrPct);

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Error:");
    Console.WriteLine(ex.ToString());
    return 100;
}

static WorksheetPart? GetWorksheetPartByName(WorkbookPart workbookPart, string sheetName)
{
    var sheets = workbookPart.Workbook.Sheets!.Elements<Sheet>();
    var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
    if (sheet == null) return null;
    if (sheet.Id == null) return null;
    return (WorksheetPart?)workbookPart.GetPartById(sheet.Id);
}

static string? GetCellValue(Cell cell, SharedStringTablePart? sstPart)
{
    if (cell == null) return null;

    var cellValue = cell.CellValue?.InnerText;
    if (cell.DataType != null)
    {
        if (cell.DataType.Value == CellValues.SharedString)
        {
            if (sstPart != null && int.TryParse(cellValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int sstIndex))
            {
                var item = sstPart.SharedStringTable?.ElementAtOrDefault(sstIndex);
                if (item != null)
                    return item.InnerText;
            }

            return null;
        }

        if (cell.DataType.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text ?? cell.InnerText;
        }

        return cellValue;
    }

    // Тип не указан — как правило число/общий
    return cellValue;
}

static int GetColumnIndex(string? cellRef)
{
    // "A1" -> 0, "B1" -> 1, ..., "AA1" -> 26
    if (string.IsNullOrEmpty(cellRef)) return 0;
    int i = 0;
    while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
    var letters = cellRef.Substring(0, i).ToUpperInvariant();

    int col = 0;
    foreach (char c in letters)
    {
        col = col * 26 + (c - 'A' + 1);
    }

    return col - 1;
}

static string? Get(Dictionary<int, string?> row, int idx)
{
    return row.TryGetValue(idx, out var v) ? v : null;
}

static decimal ParseDecimal(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return 0m;
    if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
    if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;
    return 0m;
}

static long ParseLong(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return 0L;
    if (long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return (long)d;
    return 0L;
}

static double ParseDouble(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return 0.0;
    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
    if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;
    return 0.0;
}

static void PrintTable(decimal spend, double drrPercent, long clicks, double ctrPercent)
{
    var headers = new[] { "Показатель", "Значение" };
    var rows = new List<(string, string)>
    {
        ("Расход (₽)", spend.ToString("N2", CultureInfo.InvariantCulture)),
        ("ДРР (%)", drrPercent.ToString("N2", CultureInfo.InvariantCulture)),
        ("Клики", clicks.ToString("N0", CultureInfo.InvariantCulture)),
        ("CTR (%)", ctrPercent.ToString("N2", CultureInfo.InvariantCulture))
    };

    int col1W = Math.Max(headers[0].Length, rows.Max(r => r.Item1.Length));
    int col2W = Math.Max(headers[1].Length, rows.Max(r => r.Item2.Length));

    string sep = new string('-', col1W + col2W + 5);
    Console.WriteLine(sep);
    Console.WriteLine($"| {headers[0].PadRight(col1W)} | {headers[1].PadRight(col2W)} |");
    Console.WriteLine(sep);
    foreach (var (k, v) in rows)
    {
        Console.WriteLine($"| {k.PadRight(col1W)} | {v.PadRight(col2W)} |");
    }

    Console.WriteLine(sep);
}
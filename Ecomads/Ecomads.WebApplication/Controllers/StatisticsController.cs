using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/statistics")]
public class StatisticsController : ControllerBase
{
    private readonly EcomadsDbContext _context;

    public StatisticsController(EcomadsDbContext context)
    {
        _context = context;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadStatistics([FromForm] IFormFile file,
        [FromForm] DateTime startDate, [FromForm] DateTime endDate)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty.");

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = doc.WorkbookPart!;
        var sstPart = wbPart.SharedStringTablePart;
        var wsPart = wbPart.Workbook.Sheets!.Elements<Sheet>().FirstOrDefault() != null
            ? (WorksheetPart)wbPart.GetPartById(wbPart.Workbook.Sheets!.Elements<Sheet>().First().Id!)
            : null;

        if (wsPart == null) return BadRequest("Sheet not found.");

        var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData == null) return BadRequest("Sheet has no data.");

        var rows = new List<Dictionary<int, string?>>();
        foreach (var row in sheetData.Elements<Row>())
        {
            var dict = new Dictionary<int, string?>();
            foreach (var cell in row.Elements<Cell>())
            {
                int colIdx = GetColumnIndex(cell.CellReference?.Value);
                dict[colIdx] = GetCellValue(cell, sstPart);
            }

            if (dict.Count > 0) rows.Add(dict);
        }

        var headerRow = rows[0];
        var headerIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in headerRow)
        {
            var header = kvp.Value?.Trim();
            if (!string.IsNullOrEmpty(header)) headerIndexByName[header] = kvp.Key;
        }

        int idxSpend = headerIndexByName["Затраты, RUB"];
        int idxRevenue = headerIndexByName["Заказов на сумму, RUB"];
        int idxClicks = headerIndexByName["Клики"];
        int idxCtr = headerIndexByName["CTR(%)"];

        var statsToAdd = new List<CompaignStatistics>();

        foreach (var row in rows.Skip(1))
        {
            if (!row.TryGetValue(headerIndexByName["Название"], out var name) ||
                string.IsNullOrWhiteSpace(name))
                continue;

            if (name.Contains("Всего", StringComparison.OrdinalIgnoreCase))
                continue;

            // 👉 пытаемся взять номер (проверь название колонки!)
            var number = headerIndexByName.ContainsKey("Номенклатура")
                ? Get(row, headerIndexByName["Номенклатура"])
                : name; // fallback

            if (string.IsNullOrWhiteSpace(number))
                continue;

            var spend = ParseDecimal(Get(row, idxSpend));
            var revenue = ParseDecimal(Get(row, idxRevenue));
            var clicks = ParseLong(Get(row, idxClicks));
            var ctr = ParseDouble(Get(row, idxCtr));

            if (spend == 0 && revenue == 0 && clicks == 0)
                continue;

            var drr = revenue > 0 ? (double)(spend / revenue * 100) : 0;

            // 🔥 1. Ищем кампанию
            var campaign = await _context.Compaigns
                .FirstOrDefaultAsync(c => c.Number == number);

            if (campaign == null)
            {
                campaign = new Compaign
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Number = number
                };

                _context.Compaigns.Add(campaign);
            }
            else
            {
                // 👉 можно обновить имя (если поменялось)
                campaign.Name = name;
            }

            // 🔥 2. Проверяем статистику за период
            var existingStat = await _context.CompaignStatistics
                .FirstOrDefaultAsync(s =>
                    s.CompaignId == campaign.Id &&
                    s.StartDate == DateTime.SpecifyKind(startDate, DateTimeKind.Utc) &&
                    s.EndDate == DateTime.SpecifyKind(endDate, DateTimeKind.Utc));

            if (existingStat != null)
            {
                // ✅ MERGE (перезапись)
                existingStat.Spend = (float)spend;
                existingStat.Revenue = (float)revenue;
                existingStat.Clicks = (float)clicks;
                existingStat.Ctr = (float)ctr;
                existingStat.Drr = (float)drr;
            }
            else
            {
                statsToAdd.Add(new CompaignStatistics
                {
                    CompaignId = campaign.Id,
                    StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
                    EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc),

                    Spend = (float)spend,
                    Revenue = (float)revenue,
                    Clicks = (float)clicks,
                    Ctr = (float)ctr,
                    Drr = (float)drr
                });
            }
        }

        if (statsToAdd.Count > 0)
        {
            _context.CompaignStatistics.AddRange(statsToAdd);
        }

        await _context.SaveChangesAsync();

        return Ok();
    }
    
    [HttpPost("upload-keywords")]
public async Task<IActionResult> UploadKeywordStats(IFormFile file)
{
    if (file == null || file.Length == 0)
        return BadRequest("File is empty");

    using var stream = file.OpenReadStream();
    using var doc = SpreadsheetDocument.Open(stream, false);

    var wbPart = doc.WorkbookPart!;
    var sstPart = wbPart.SharedStringTablePart;

    var sheet = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
    var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);

    var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
    if (sheetData == null)
        return BadRequest("Sheet has no data");

    var rows = new List<Dictionary<int, string?>>();

    foreach (var row in sheetData.Elements<Row>())
    {
        var dict = new Dictionary<int, string?>();
        foreach (var cell in row.Elements<Cell>())
        {
            int colIdx = GetColumnIndex(cell.CellReference?.Value);
            dict[colIdx] = GetCellValue(cell, sstPart);
        }

        if (dict.Count > 0)
            rows.Add(dict);
    }

    // 🔥 Заголовки
    var headerRow = rows.First(r =>
        r.Values.Any(v => v != null && v.Contains("Показы")));
    var headerIndex = headerRow
        .Where(x => !string.IsNullOrWhiteSpace(x.Value))
        .ToDictionary(x => x.Value!.Trim(), x => x.Key, StringComparer.OrdinalIgnoreCase);

    // 🔥 Индексы колонок
    int idxPhrase = GetColumn(headerIndex, "Фраз", "Кластер", "Запрос");
    int idxFreq = GetColumn(headerIndex, "Частота");
    int idxCpm = GetColumn(headerIndex, "CPM");
    int idxAvgPos = GetColumn(headerIndex, "позиция");
    int idxImpr = GetColumn(headerIndex, "Показы");
    int idxClicks = GetColumn(headerIndex, "Клики");
    int idxCtr = GetColumn(headerIndex, "CTR");
    int idxSpend = GetColumn(headerIndex, "Затраты");
    int idxOrders = GetColumn(headerIndex, "Заказы");
    int idxRevenue = GetColumn(headerIndex, "Выручка");

    var result = new List<KeywordStatDto>();

    foreach (var row in rows.Skip(1))
    {
        var phrase = Get(row, idxPhrase);

        if (string.IsNullOrWhiteSpace(phrase))
            continue;

        // ❗ фильтр мусора
        if (phrase.Contains("Итого", StringComparison.OrdinalIgnoreCase))
            continue;

        var freq = ParseInt(Get(row, idxFreq));
        var cpm = ParseDecimal(Get(row, idxCpm));
        var avgPos = ParseDouble(Get(row, idxAvgPos));
        var impressions = ParseInt(Get(row, idxImpr));
        var clicks = ParseInt(Get(row, idxClicks));
        var ctr = ParseDouble(Get(row, idxCtr));
        var spend = ParseDecimal(Get(row, idxSpend));
        var orders = ParseInt(Get(row, idxOrders));
        var revenue = ParseDecimal(Get(row, idxRevenue));

        if (impressions == 0 && clicks == 0 && spend == 0)
            continue;

        var drr = revenue > 0 ? (double)(spend / revenue * 100) : 0;

        result.Add(new KeywordStatDto
        {
            Phrase = phrase,
            Frequency = freq,
            Cpm = cpm,
            AvgPosition = avgPos,
            Impressions = impressions,
            Clicks = clicks,
            Ctr = ctr,
            Spend = spend,
            Orders = orders,
            Revenue = revenue,
            Drr = drr
        });
    }

    return Ok(result);
}

    int GetColumn(Dictionary<string, int> headerIndex, params string[] possibleNames)
    {
        foreach (var name in possibleNames)
        {
            var key = headerIndex.Keys
                .FirstOrDefault(k => k.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (key != null)
                return headerIndex[key];
        }

        throw new Exception($"Column not found: {string.Join(", ", possibleNames)}");
    }

    private static string? GetCellValue(Cell cell, SharedStringTablePart? sstPart)
    {
        var val = cell.CellValue?.InnerText;
        if (cell.DataType?.Value == CellValues.SharedString && sstPart != null && int.TryParse(val, out int idx))
            return sstPart.SharedStringTable?.ElementAtOrDefault(idx)?.InnerText;
        return val;
    }

    private static int GetColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        int col = 0;
        foreach (char c in cellRef.Substring(0, i).ToUpperInvariant()) col = col * 26 + (c - 'A' + 1);
        return col - 1;
    }

    private static string? Get(Dictionary<int, string?> row, int idx) => row.TryGetValue(idx, out var v) ? v : null;

    private static decimal? ParseDecimal(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static long? ParseLong(string? s) =>
        long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    
    static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Replace(" ", "");
        return int.TryParse(value, out var v) ? v : null;
    }
}
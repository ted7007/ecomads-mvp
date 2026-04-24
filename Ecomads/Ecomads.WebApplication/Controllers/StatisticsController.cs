using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Ecomads.WebApplication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecomads.WebApplication.Services;
using System.Globalization;
using System.Security.Claims;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Route("api/statistics")]
public class StatisticsController : ControllerBase
{
    private readonly EcomadsDbContext _context;
    private readonly IStatisticsQueue _queue;

    public StatisticsController(EcomadsDbContext context, IStatisticsQueue queue)
    {
        _context = context;
        _queue = queue;
    }
    
    [HttpPost("upload")]
    [Authorize]
    public async Task<IActionResult> UploadStatistics([FromForm] IFormFile file,
        [FromForm] DateTime startDate, [FromForm] DateTime endDate)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var sellerId))
        {
            return Unauthorized(new { message = "Недействительный токен" });
        }
        
        var store = _context.Stores.First(s => s.SellerId == sellerId);
        
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

            var number = headerIndexByName.ContainsKey("Номенклатура")
                ? Get(row, headerIndexByName["Номенклатура"])
                : name;

            if (string.IsNullOrWhiteSpace(number))
                continue;

            var spend = ParseDecimal(Get(row, idxSpend));
            var revenue = ParseDecimal(Get(row, idxRevenue));
            var clicks = ParseLong(Get(row, idxClicks));
            var ctr = ParseDouble(Get(row, idxCtr));

            if (spend == 0 && revenue == 0 && clicks == 0)
                continue;

            var drr = revenue > 0 ? (double)(spend / revenue * 100) : 0;

            var campaign = await _context.Compaigns
                .FirstOrDefaultAsync(c => c.Number == number);

            if (campaign == null)
            {
                campaign = new Compaign
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Number = number,
                    StoreId = store.Id
                };

                _context.Compaigns.Add(campaign);
            }
            else
            {
                campaign.Name = name;
            }

            var existingStat = await _context.CompaignStatistics
                .FirstOrDefaultAsync(s =>
                    s.CompaignId == campaign.Id &&
                    s.StartDate == DateTime.SpecifyKind(startDate, DateTimeKind.Utc) &&
                    s.EndDate == DateTime.SpecifyKind(endDate, DateTimeKind.Utc));

            if (existingStat != null)
            {
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

    [HttpGet("keywords/{campaignId}")]
    public async Task<IActionResult> GetKeywordStatistics(
        Guid campaignId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var query = _context.KeywordStatistics
            .Where(s => s.CompaignId == campaignId);

        if (startDate.HasValue)
            query = query.Where(s => s.StartDate >= DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc));
        
        if (endDate.HasValue)
            query = query.Where(s => s.EndDate <= DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc));

        var stats = await query
            .GroupBy(s => s.Phrase)
            .Select(g => new KeywordStatDto
            {
                Phrase = g.Key,
                Frequency = g.Sum(s => s.Frequency),
                Cpm = g.Average(s => s.Cpm),
                AvgPosition = g.Average(s => s.AvgPosition),
                Impressions = g.Sum(s => s.Impressions),
                Clicks = g.Sum(s => s.Clicks),
                Ctr = g.Average(s => s.Ctr),
                Spend = g.Sum(s => s.Spend),
                Orders = g.Sum(s => s.Orders),
                Revenue = g.Sum(s => s.Revenue),
                Drr = g.Average(s => s.Drr)
            })
            .ToListAsync();

        return Ok(stats);
    }

    [HttpPost("upload-keywords")]
    public async Task<IActionResult> UploadKeywordStats([FromForm] IFormFile file,
        [FromForm] DateTime startDate, [FromForm] DateTime endDate, [FromForm] Guid campaignId)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty");

        var campaign = await _context.Compaigns.FindAsync(campaignId);
        if (campaign == null)
            return BadRequest($"Campaign with ID {campaignId} does not exist.");

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

        var existingStats = await _context.KeywordStatistics
            .Where(s => s.CompaignId == campaignId &&
                        s.StartDate == DateTime.SpecifyKind(startDate, DateTimeKind.Utc) &&
                        s.EndDate == DateTime.SpecifyKind(endDate, DateTimeKind.Utc))
            .ToListAsync();

        var statsToAdd = new List<KeywordStatistics>();

        foreach (var row in rows.Skip(2))
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

            var existingStat = existingStats.FirstOrDefault(s => s.Phrase == phrase);

            if (existingStat != null)
            {
                // ✅ MERGE (перезапись)
                existingStat.StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
                existingStat.EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
                existingStat.Frequency = freq ?? 0;
                existingStat.Cpm = (decimal?)(cpm ?? 0);
                existingStat.AvgPosition = (double?)(avgPos ?? 0);
                existingStat.Impressions = impressions ?? 0;
                existingStat.Clicks = clicks ?? 0;
                existingStat.Ctr = (double?)(ctr ?? 0);
                existingStat.Spend = (decimal?)(spend ?? 0);
                existingStat.Orders = orders ?? 0;
                existingStat.Revenue = (decimal?)(revenue ?? 0);
                existingStat.Drr = (double?)drr;
            }
            else
            {
                statsToAdd.Add(new KeywordStatistics
                {
                    Id = Guid.NewGuid(),
                    CompaignId = campaignId,
                    StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
                    EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc),
                    Phrase = phrase,
                    Frequency = freq ?? 0,
                    Cpm = (decimal?)(cpm ?? 0),
                    AvgPosition = (double?)(avgPos ?? 0),
                    Impressions = impressions ?? 0,
                    Clicks = clicks ?? 0,
                    Ctr = (double?)(ctr ?? 0),
                    Spend = (decimal?)(spend ?? 0),
                    Orders = orders ?? 0,
                    Revenue = (decimal?)(revenue ?? 0),
                    Drr = (double?)drr
                });
            }
        }

        if (statsToAdd.Count > 0)
        {
            _context.KeywordStatistics.AddRange(statsToAdd);
        }
        await _context.SaveChangesAsync();
        
        _queue.Enqueue(new StatisticsJob(campaignId, startDate, endDate));
        return Ok();
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

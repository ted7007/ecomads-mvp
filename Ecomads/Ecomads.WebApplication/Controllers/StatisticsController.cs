using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Models;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<IActionResult> UploadStatistics([FromForm] IFormFile file, [FromForm] Guid compaignId, [FromForm] DateTime startDate, [FromForm] DateTime endDate)
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

        var totalRow = rows.Skip(1).FirstOrDefault(r => 
            r.TryGetValue(headerIndexByName["Название"], out var val) && 
            val?.Trim().Equals("Всего по кампании", StringComparison.OrdinalIgnoreCase) == true);

        if (totalRow == null) return BadRequest("Total row not found.");

        var stats = new CompaignStatistics
        {
            CompaignId = compaignId,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc),
            Spend = (float)ParseDecimal(Get(totalRow, idxSpend)),
            Revenue = (float)ParseDecimal(Get(totalRow, idxRevenue)),
            Clicks = (float)ParseLong(Get(totalRow, idxClicks)),
            Ctr = (float)ParseDouble(Get(totalRow, idxCtr)),
            Drr = (float)(ParseDecimal(Get(totalRow, idxSpend)) > 0 ? (double)(ParseDecimal(Get(totalRow, idxSpend)) / ParseDecimal(Get(totalRow, idxRevenue)) * 100) : 0)
        };

        _context.CompaignStatistics.Add(stats);
        await _context.SaveChangesAsync();

        return Ok();
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
        int i = 0; while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        int col = 0; foreach (char c in cellRef.Substring(0, i).ToUpperInvariant()) col = col * 26 + (c - 'A' + 1);
        return col - 1;
    }

    private static string? Get(Dictionary<int, string?> row, int idx) => row.TryGetValue(idx, out var v) ? v : null;
    private static decimal ParseDecimal(string? s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    private static long ParseLong(string? s) => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0L;
    private static double ParseDouble(string? s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
}

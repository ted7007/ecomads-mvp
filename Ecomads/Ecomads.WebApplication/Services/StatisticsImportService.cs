using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Services;

public interface IStatisticsImportService
{
    Task<StatisticsImportResult> ImportAsync(
        Guid sellerId,
        IFormFile campaignNamesFile,
        IReadOnlyCollection<IFormFile> wbStatisticsFiles,
        IReadOnlyCollection<IFormFile> evirmaFiles,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

public sealed class StatisticsImportService : IStatisticsImportService
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Regex WbCampaignIdRegex = new(@"statistics[-_](?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EvirmaCampaignIdRegex = new(@"(?:Экспорт|Export)_(?<id>\d+)_feature_cmp_advert_keywords_stats", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly EcomadsDbContext _dbContext;

    public StatisticsImportService(EcomadsDbContext dbContext) => _dbContext = dbContext;

    public async Task<StatisticsImportResult> ImportAsync(
        Guid sellerId,
        IFormFile campaignNamesFile,
        IReadOnlyCollection<IFormFile> wbStatisticsFiles,
        IReadOnlyCollection<IFormFile> evirmaFiles,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ValidateFiles(campaignNamesFile, wbStatisticsFiles, evirmaFiles, startDate, endDate);

        var campaignNames = await ParseCampaignNamesAsync(campaignNamesFile, cancellationToken);
        var wbReports = new Dictionary<string, WbCampaignReport>(StringComparer.Ordinal);
        foreach (var file in wbStatisticsFiles)
        {
            var campaignId = GetCampaignId(file.FileName, WbCampaignIdRegex, "WB-статистики");
            if (!wbReports.TryAdd(campaignId, await ParseWbReportAsync(file, campaignId, cancellationToken)))
                throw new StatisticsImportValidationException($"Для кампании {campaignId} загружено несколько WB-отчетов.");
        }

        var evirmaReports = new Dictionary<string, List<EvirmaKeywordRow>>(StringComparer.Ordinal);
        foreach (var file in evirmaFiles)
        {
            var campaignId = GetCampaignId(file.FileName, EvirmaCampaignIdRegex, "Эвирмы");
            if (!evirmaReports.TryAdd(campaignId, await ParseEvirmaReportAsync(file, cancellationToken)))
                throw new StatisticsImportValidationException($"Для кампании {campaignId} загружено несколько отчетов Эвирмы.");
        }

        var wbIds = wbReports.Keys.ToHashSet(StringComparer.Ordinal);
        var evirmaIds = evirmaReports.Keys.ToHashSet(StringComparer.Ordinal);
        var unknown = wbIds.Concat(evirmaIds).Where(id => !campaignNames.ContainsKey(id)).Distinct().ToArray();
        if (unknown.Length > 0)
            throw new StatisticsImportValidationException($"Кампании из статистики отсутствуют в отчете «Название кампании»: {string.Join(", ", unknown)}.");
        if (!wbIds.SetEquals(evirmaIds))
        {
            var withoutEvirma = wbIds.Except(evirmaIds).ToArray();
            var withoutWb = evirmaIds.Except(wbIds).ToArray();
            throw new StatisticsImportValidationException(
                $"Для каждой кампании нужны оба отчета. Без Эвирмы: {string.Join(", ", withoutEvirma)}. Без WB: {string.Join(", ", withoutWb)}.");
        }

        var store = await _dbContext.Stores.SingleOrDefaultAsync(store => store.SellerId == sellerId, cancellationToken)
            ?? throw new StatisticsImportValidationException("Для текущего пользователя не найден магазин.");
        var startDateUtc = UtcDate.FromDateOnly(startDate);
        var endDateUtc = UtcDate.FromDateOnly(endDate);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existingCampaigns = await _dbContext.Campaigns.Where(x => x.StoreId == store.Id).ToListAsync(cancellationToken);
        foreach (var campaign in existingCampaigns)
            campaign.IsActive = false;

        var campaignByWbId = existingCampaigns.ToDictionary(x => x.WbCampaignId, StringComparer.Ordinal);
        foreach (var (wbCampaignId, name) in campaignNames)
        {
            if (!campaignByWbId.TryGetValue(wbCampaignId, out var campaign))
            {
                campaign = new Campaign { Id = Guid.NewGuid(), StoreId = store.Id, WbCampaignId = wbCampaignId, Name = name, CreatedAt = DateTime.UtcNow };
                _dbContext.Campaigns.Add(campaign);
                campaignByWbId.Add(wbCampaignId, campaign);
            }
            else
                campaign.Name = name;

            campaign.IsActive = true;
            campaign.LastSeenAt = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var importedCampaigns = wbIds.Select(id => campaignByWbId[id]).ToList();
        var importedCampaignIds = importedCampaigns.Select(x => x.Id).ToArray();
        await _dbContext.CampaignStatistics.Where(x => importedCampaignIds.Contains(x.CampaignId) && x.StartDate == startDateUtc && x.EndDate == endDateUtc).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.CampaignNomenclatureStatistics.Where(x => importedCampaignIds.Contains(x.CampaignId) && x.StartDate == startDateUtc && x.EndDate == endDateUtc).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.KeywordStatistics.Where(x => importedCampaignIds.Contains(x.CampaignId) && x.StartDate == startDateUtc && x.EndDate == endDateUtc).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Recommendations.Where(x => importedCampaignIds.Contains(x.CampaignId)).ExecuteDeleteAsync(cancellationToken);

        var existingNomenclatures = await _dbContext.Nomenclatures.Where(x => x.StoreId == store.Id).ToListAsync(cancellationToken);
        var nomenclatureByWbId = existingNomenclatures.ToDictionary(x => x.WbNomenclatureId, StringComparer.Ordinal);
        var nomenclatureStats = new List<CampaignNomenclatureStatistics>();
        var campaignStats = new List<CampaignStatistics>();
        var keywordStats = new List<KeywordStatistics>();

        foreach (var (wbCampaignId, report) in wbReports)
        {
            var campaign = campaignByWbId[wbCampaignId];
            foreach (var row in report.Nomenclatures)
            {
                if (!nomenclatureByWbId.TryGetValue(row.WbNomenclatureId, out var nomenclature))
                {
                    nomenclature = new Nomenclature { Id = Guid.NewGuid(), StoreId = store.Id, WbNomenclatureId = row.WbNomenclatureId, Name = row.Name };
                    _dbContext.Nomenclatures.Add(nomenclature);
                    nomenclatureByWbId.Add(row.WbNomenclatureId, nomenclature);
                }
                else if (!string.IsNullOrWhiteSpace(row.Name))
                    nomenclature.Name = row.Name;

                nomenclatureStats.Add(new CampaignNomenclatureStatistics
                {
                    CampaignId = campaign.Id, NomenclatureId = nomenclature.Id, StartDate = startDateUtc, EndDate = endDateUtc,
                    Spend = row.Spend, Revenue = row.Revenue, Impressions = row.Impressions, Clicks = row.Clicks, Carts = row.Carts,
                    Orders = row.Orders, Cancellations = row.Cancellations, Ctr = row.Ctr, Cr = row.Cr, Cpm = row.Cpm, Cpc = row.Cpc, Cpo = row.Cpo, AveragePosition = row.AveragePosition
                });
            }

            var totals = report.Nomenclatures.Aggregate(new WbTotals(), (current, row) => current.Add(row));
            campaignStats.Add(new CampaignStatistics
            {
                CampaignId = campaign.Id, StartDate = startDateUtc, EndDate = endDateUtc, Type = CampaignStatisticsType.General,
                Spend = (float)totals.Spend, Revenue = (float)totals.Revenue, Clicks = totals.Clicks, Impressions = totals.Impressions,
                Carts = totals.Carts, Orders = totals.Orders, Cancellations = totals.Cancellations,
                Ctr = totals.Impressions > 0 ? (float)(totals.Clicks * 100m / totals.Impressions) : 0,
                Drr = totals.Revenue > 0 ? (float)(totals.Spend * 100m / totals.Revenue) : 0
            });

            foreach (var row in evirmaReports[wbCampaignId])
            {
                keywordStats.Add(new KeywordStatistics
                {
                    Id = Guid.NewGuid(), CampaignId = campaign.Id, StartDate = startDateUtc, EndDate = endDateUtc,
                    Phrase = row.Phrase, NormalizedPhrase = NormalizePhrase(row.Phrase), BidCpm = row.BidCpm, Frequency = row.Frequency,
                    Cpm = row.Cpm, AvgPosition = row.AveragePosition.HasValue ? (double)row.AveragePosition.Value : null, Impressions = row.Impressions, Clicks = row.Clicks, Ctr = row.Ctr.HasValue ? (double)row.Ctr.Value : null,
                    Spend = row.Spend, Baskets = row.Baskets, Orders = row.Orders, Cpc = row.Cpc, Cpo = row.Cpo, Revenue = row.Revenue,
                    Drr = row.Revenue is > 0 ? (double)(row.Spend / row.Revenue.Value * 100m) : null
                });
            }
        }

        _dbContext.CampaignNomenclatureStatistics.AddRange(nomenclatureStats);
        _dbContext.CampaignStatistics.AddRange(campaignStats);
        _dbContext.KeywordStatistics.AddRange(keywordStats);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StatisticsImportResult(importedCampaigns.Count, nomenclatureStats.Count, keywordStats.Count);
    }

    private static void ValidateFiles(IFormFile campaignNamesFile, IReadOnlyCollection<IFormFile> wbFiles, IReadOnlyCollection<IFormFile> evirmaFiles, DateOnly startDate, DateOnly endDate)
    {
        if (campaignNamesFile?.Length <= 0 || wbFiles.Count == 0 || evirmaFiles.Count == 0)
            throw new StatisticsImportValidationException("Загрузите отчет с названиями кампаний, хотя бы один WB-отчет и хотя бы один отчет Эвирмы.");
        if (startDate > endDate)
            throw new StatisticsImportValidationException("Дата начала периода не может быть позже даты окончания.");
        if (!AllXlsx(wbFiles.Append(campaignNamesFile).Concat(evirmaFiles)))
            throw new StatisticsImportValidationException("Поддерживаются только файлы .xlsx.");
    }

    private static bool AllXlsx(IEnumerable<IFormFile> files) => files.All(file => file.Length > 0 && string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase));
    private static string GetCampaignId(string fileName, Regex regex, string reportName)
    {
        var match = regex.Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? match.Groups["id"].Value : throw new StatisticsImportValidationException($"Не удалось определить ID кампании из имени файла {reportName}: {fileName}.");
    }

    private static async Task<Dictionary<string, string>> ParseCampaignNamesAsync(IFormFile file, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(file, ct);
        var header = FindHeader(rows, "ID", "Кампания");
        var id = RequiredColumn(header, "ID");
        var name = RequiredColumn(header, "Кампания");
        return rows.Skip(header.RowIndex + 1)
            .Select(row => (Id: Value(row, id), Name: Value(row, name)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Id.Trim(), x => x.Name.Trim(), StringComparer.Ordinal);
    }

    private static async Task<WbCampaignReport> ParseWbReportAsync(IFormFile file, string campaignId, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(file, ct);
        var header = FindHeader(rows, "Номенклатура", "Тип конверсии", "Затраты");
        var nomenclature = RequiredColumn(header, "Номенклатура");
        var conversion = RequiredColumn(header, "Тип конверсии");
        var name = FindColumn(header, "Название");
        var parsed = new List<WbNomenclatureRow>();
        foreach (var row in rows.Skip(header.RowIndex + 1))
        {
            var conversionType = Value(row, conversion);
            if (!string.Equals(conversionType?.Trim(), "Прямая", StringComparison.OrdinalIgnoreCase))
                continue;
            var wbNomenclatureId = Value(row, nomenclature)?.Trim();
            if (string.IsNullOrWhiteSpace(wbNomenclatureId)) continue;
            parsed.Add(new WbNomenclatureRow(
                wbNomenclatureId, Value(row, name)?.Trim() ?? wbNomenclatureId,
                Decimal(row, header, "Затраты"), Decimal(row, header, "Заказов на сумму"), Int(row, header, "Показы"), Int(row, header, "Клики"),
                Int(row, header, "Добавления в корзину"), Int(row, header, "Заказы"), Int(row, header, "Отмены"), DecimalOrNull(row, header, "CTR"),
                DecimalOrNull(row, header, "CR"), DecimalOrNull(row, header, "CPM"), DecimalOrNull(row, header, "CPC"), DecimalOrNull(row, header, "CPO"), DecimalOrNull(row, header, "позиция")));
        }
        if (parsed.Count == 0) throw new StatisticsImportValidationException($"В WB-отчете кампании {campaignId} не найдено номенклатур с типом конверсии «Прямая».");
        return new WbCampaignReport(parsed);
    }

    private static async Task<List<EvirmaKeywordRow>> ParseEvirmaReportAsync(IFormFile file, CancellationToken ct)
    {
        var rows = await ReadRowsAsync(file, ct);
        var header = FindHeader(rows, "Кластеры", "Показы");
        var phrase = RequiredColumn(header, "Кластеры");
        var result = new Dictionary<string, EvirmaKeywordRow>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(header.RowIndex + 1))
        {
            var value = Value(row, phrase)?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Equals("Итого", StringComparison.OrdinalIgnoreCase) || value.Equals("Всего", StringComparison.OrdinalIgnoreCase)) continue;
            var item = new EvirmaKeywordRow(value, DecimalOrNull(row, header, "Ставка CPM"), IntOrNull(row, header, "Частота"), DecimalOrNull(row, header, "Средняя рекл. Позиция"),
                Int(row, header, "Показы"), Int(row, header, "Клики"), DecimalOrNull(row, header, "CTR"), DecimalOrNull(row, header, "CPM"), Decimal(row, header, "Затраты"), IntOrNull(row, header, "Корзины РК"),
                Int(row, header, "Заказы РК"), DecimalOrNull(row, header, "CPC"), DecimalOrNull(row, header, "CPO РК"), DecimalOrNull(row, header, "Выручка РК"));
            result[NormalizePhrase(value)] = item;
        }
        if (result.Count == 0) throw new StatisticsImportValidationException("В отчете Эвирмы не найдено ключевых фраз.");
        return result.Values.ToList();
    }

    private static async Task<List<Dictionary<int, string?>>> ReadRowsAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct); stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart ?? throw new StatisticsImportValidationException("Файл Excel не содержит книги.");
        var sheet = workbook.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new StatisticsImportValidationException("В файле Excel нет листа.");
        var worksheet = (WorksheetPart)workbook.GetPartById(sheet.Id!);
        var data = worksheet.Worksheet.GetFirstChild<SheetData>() ?? throw new StatisticsImportValidationException("Лист Excel не содержит данных.");
        return data.Elements<Row>().Select(row => row.Elements<Cell>().ToDictionary(cell => GetColumnIndex(cell.CellReference?.Value), cell => GetCellValue(cell, workbook.SharedStringTablePart))).Where(row => row.Count > 0).ToList();
    }

    private static Header FindHeader(IReadOnlyList<Dictionary<int, string?>> rows, params string[] required)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var columns = rows[index].Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Value!.Trim(), x => x.Key, StringComparer.OrdinalIgnoreCase);
            if (required.All(value => columns.Keys.Any(key => key.Contains(value, StringComparison.OrdinalIgnoreCase)))) return new Header(index, columns);
        }
        throw new StatisticsImportValidationException($"Не найдены обязательные колонки: {string.Join(", ", required)}.");
    }
    private static int RequiredColumn(Header header, string name) => FindColumn(header, name) ?? throw new StatisticsImportValidationException($"Не найдена колонка «{name}».");
    private static int? FindColumn(Header header, string name)
    {
        var exact = header.Columns.FirstOrDefault(x => string.Equals(x.Key.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact.Key)) return exact.Value;
        var partial = header.Columns.FirstOrDefault(x => x.Key.Contains(name, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(partial.Key) ? null : partial.Value;
    }
    private static string? Value(Dictionary<int, string?> row, int? index) => index.HasValue && row.TryGetValue(index.Value, out var value) ? value : null;
    private static decimal Decimal(Dictionary<int, string?> row, Header header, string column) => ParseDecimal(Value(row, FindColumn(header, column))) ?? 0m;
    private static decimal? DecimalOrNull(Dictionary<int, string?> row, Header header, string column) => ParseDecimal(Value(row, FindColumn(header, column)));
    private static int Int(Dictionary<int, string?> row, Header header, string column) => IntOrNull(row, header, column) ?? 0;
    private static int? IntOrNull(Dictionary<int, string?> row, Header header, string column) { var value = ParseDecimal(Value(row, FindColumn(header, column))); return value.HasValue ? decimal.ToInt32(value.Value) : null; }
    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Replace("\u00A0", string.Empty).Replace(" ", string.Empty).Replace("%", string.Empty);
        return decimal.TryParse(value, NumberStyles.Any, RussianCulture, out var result) || decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : null;
    }
    private static string NormalizePhrase(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    private static string? GetCellValue(Cell cell, SharedStringTablePart? strings) => cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.InnerText, out var index) ? strings?.SharedStringTable?.ElementAtOrDefault(index)?.InnerText : cell.CellValue?.InnerText;
    private static int GetColumnIndex(string? reference) { if (string.IsNullOrWhiteSpace(reference)) return 0; var result = 0; foreach (var letter in reference.TakeWhile(char.IsLetter)) result = result * 26 + char.ToUpperInvariant(letter) - 'A' + 1; return result - 1; }

    private sealed record Header(int RowIndex, Dictionary<string, int> Columns);
    private sealed record WbCampaignReport(List<WbNomenclatureRow> Nomenclatures);
    private sealed record WbNomenclatureRow(string WbNomenclatureId, string Name, decimal Spend, decimal Revenue, int Impressions, int Clicks, int Carts, int Orders, int Cancellations, decimal? Ctr, decimal? Cr, decimal? Cpm, decimal? Cpc, decimal? Cpo, decimal? AveragePosition);
    private sealed record EvirmaKeywordRow(string Phrase, decimal? BidCpm, int? Frequency, decimal? AveragePosition, int Impressions, int Clicks, decimal? Ctr, decimal? Cpm, decimal Spend, int? Baskets, int Orders, decimal? Cpc, decimal? Cpo, decimal? Revenue);
    private sealed record WbTotals(decimal Spend = 0, decimal Revenue = 0, int Impressions = 0, int Clicks = 0, int Carts = 0, int Orders = 0, int Cancellations = 0) { public WbTotals Add(WbNomenclatureRow row) => this with { Spend = Spend + row.Spend, Revenue = Revenue + row.Revenue, Impressions = Impressions + row.Impressions, Clicks = Clicks + row.Clicks, Carts = Carts + row.Carts, Orders = Orders + row.Orders, Cancellations = Cancellations + row.Cancellations }; }
}

public sealed record StatisticsImportResult(int CampaignsCount, int NomenclaturesCount, int KeywordsCount);
public sealed class StatisticsImportValidationException(string message) : Exception(message);

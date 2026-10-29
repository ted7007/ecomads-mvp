using SkiaSharp;

namespace Ecomads.WebApplication.Services.Telegram;

/// <summary>Everything the summary picture shows. A null number is drawn as a dash.</summary>
public sealed record TelegramCardData(
    string StoreName,
    string Title,
    string DateLabel,
    string? UpdatedAtText,
    decimal? Spend, decimal? SpendChangePercent,
    decimal? OrderSum, decimal? OrderSumChangePercent,
    decimal? Drr, decimal? DrrChangePoints,
    decimal? Ctr,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<decimal?> SpendSeries,
    IReadOnlyList<decimal?> DrrSeries,
    IReadOnlyList<decimal?>? PreviousDrrSeries,
    IReadOnlyList<TelegramSummaryCampaign> Campaigns,
    bool IsTest = false);

/// <summary>
/// Draws the summary card (key figures, two small charts, top campaigns) with SkiaSharp and the embedded Inter font.
/// If drawing fails for any reason the caller falls back to the simple chart or to text only.
/// </summary>
public static class TelegramCardRenderer
{
    private const float W = 760, Pad = 40, Scale = 2;
    private static readonly SKColor Ink = SKColor.Parse("#1C2330"), Muted = SKColor.Parse("#8791A1"),
        Line = SKColor.Parse("#E6EAF0"), Orange = SKColor.Parse("#F08A00"), Teal = SKColor.Parse("#12A094"),
        Good = SKColor.Parse("#2F9E5B"), Bad = SKColor.Parse("#D6453D");
    private static readonly Lazy<SKTypeface> Regular = new(() => Load("Inter-Regular.otf"));
    private static readonly Lazy<SKTypeface> Bold = new(() => Load("Inter-Bold.otf"));

    private static SKTypeface Load(string name)
    {
        using var stream = typeof(TelegramCardRenderer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Font resource is missing: " + name);
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("Font cannot be read: " + name);
    }

    /// <summary>Returns PNG bytes, the simple chart if the card cannot be drawn, or null when there is nothing to draw.</summary>
    public static byte[]? RenderOrFallback(TelegramCardData card)
    {
        try { return Render(card); }
        catch (Exception)
        {
            try
            {
                return TelegramChartRenderer.Render(card.Days, card.SpendSeries, card.DrrSeries, null, card.PreviousDrrSeries);
            }
            catch (Exception) { return null; }
        }
    }

    public static byte[] Render(TelegramCardData card)
    {
        var campaigns = card.Campaigns.Take(5).ToList();
        var hasCharts = card.SpendSeries.Count(x => x.HasValue) >= 2;
        float y = 0;
        var height = Pad + 40 + 70 + 2 * 112 + 14;                       // header, title, tiles
        if (hasCharts) height += 36 + 2 * 156;                           // section title + two charts
        if (campaigns.Count > 0) height += 36 + 50 + campaigns.Count * 58 + 10;
        height += 50 + Pad / 2;                                          // footer

        var info = new SKImageInfo((int)(W * Scale), (int)(height * Scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Scale(Scale);
        canvas.Clear(SKColors.White);

        // Header
        y = Pad + 26;
        DrawLogo(canvas, Pad, y - 30, 40);
        Text(canvas, card.DateLabel, W - Pad, y, 18, Regular.Value, Muted, right: true);
        y += 22 + 40;
        Text(canvas, Fit(card.Title, W - 2 * Pad, 36, Bold.Value), Pad, y, 36, Bold.Value, Ink);
        y += 12;
        Text(canvas, Fit((card.IsTest ? "Тест · демо-данные · " : string.Empty) + card.StoreName, W - 2 * Pad, 17, Regular.Value),
            Pad, y + 22, 17, Regular.Value, Muted);
        y += 22 + 14;

        // Key figures
        var tileW = (W - 2 * Pad - 14) / 2;
        Tile(canvas, Pad, y, tileW, "Расход", card.Spend.HasValue ? Rub(card.Spend.Value) + " ₽" : "—",
            card.SpendChangePercent, "%", goodWhenUp: false);
        Tile(canvas, Pad + tileW + 14, y, tileW, "Заказы", card.OrderSum.HasValue ? Rub(card.OrderSum.Value) + " ₽" : "—",
            card.OrderSumChangePercent, "%", goodWhenUp: true);
        y += 112;
        Tile(canvas, Pad, y, tileW, "ДРР от заказов", card.Drr.HasValue ? Percent(card.Drr.Value) : "—",
            card.DrrChangePoints, " п.п.", goodWhenUp: false);
        Tile(canvas, Pad + tileW + 14, y, tileW, "CTR", card.Ctr.HasValue ? Percent(card.Ctr.Value) : "—", null, "", true);
        y += 112 + 14;

        if (hasCharts)
        {
            Text(canvas, "Динамика за 7 дней", Pad, y + 24, 21, Bold.Value, Ink);
            y += 36;
            BarsPanel(canvas, Pad, y, W - 2 * Pad, 148, "Расход", card);
            y += 156;
            LinePanel(canvas, Pad, y, W - 2 * Pad, 148, "ДРР от заказов", card);
            y += 156;
        }

        if (campaigns.Count > 0)
        {
            Text(canvas, "Топ кампаний по расходу", Pad, y + 24, 21, Bold.Value, Ink);
            y += 36;
            Campaigns(canvas, Pad, y, W - 2 * Pad, campaigns);
            y += 50 + campaigns.Count * 58 + 10;
        }

        var footer = card.UpdatedAtText != null ? "Данные: " + card.UpdatedAtText : "Время обновления неизвестно";
        Text(canvas, hasCharts ? "Штриховая линия — предыдущие 7 дней" : string.Empty, Pad, y + 24, 14, Regular.Value, Muted);
        Text(canvas, footer, W - Pad, y + 24, 14, Regular.Value, Muted, right: true);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static readonly Lazy<SKBitmap?> Logo = new(() =>
    {
        try
        {
            using var stream = typeof(TelegramCardRenderer).Assembly.GetManifestResourceStream("ecomads-logo.png");
            return stream == null ? null : SKBitmap.Decode(stream);
        }
        catch (Exception) { return null; }
    });

    /// <summary>Draws the EcomAds logo with the given height; falls back to a text mark if the picture is unavailable.</summary>
    private static void DrawLogo(SKCanvas canvas, float x, float y, float height)
    {
        if (Logo.Value is { } bitmap)
        {
            var width = height * bitmap.Width / bitmap.Height;
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height), paint);
            return;
        }
        Text(canvas, "ecomads", x, y + height * 0.75f, 26, Bold.Value, Orange);
    }

    private static void Tile(SKCanvas canvas, float x, float y, float width, string label, string value,
        decimal? change, string unit, bool goodWhenUp)
    {
        Box(canvas, x, y, width, 98);
        Text(canvas, label, x + 20, y + 28, 16, Regular.Value, Muted);
        Text(canvas, Fit(value, width - 40, 32, Bold.Value), x + 20, y + 62, 32, Bold.Value, Ink);
        if (change is not { } c) return;
        var up = c >= 0;
        var good = c == 0 ? (bool?)null : up == goodWhenUp;
        var color = good == null ? Muted : good.Value ? Good : Bad;
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
        using var path = new SKPath();
        var cx = x + 27; var cy = y + 83;
        if (up) { path.MoveTo(cx - 6, cy + 5); path.LineTo(cx + 6, cy + 5); path.LineTo(cx, cy - 6); }
        else { path.MoveTo(cx - 6, cy - 5); path.LineTo(cx + 6, cy - 5); path.LineTo(cx, cy + 6); }
        path.Close();
        canvas.DrawPath(path, paint);
        var number = Math.Abs(c).ToString(unit == "%" ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        Text(canvas, $"{number}{unit} к прошлому периоду", x + 42, y + 88, 15, Regular.Value, color);
    }

    private static void BarsPanel(SKCanvas canvas, float x, float y, float width, float height, string title, TelegramCardData card)
    {
        Box(canvas, x, y, width, height);
        var last = card.SpendSeries.LastOrDefault(v => v.HasValue);
        Text(canvas, title, x + 20, y + 28, 16, Regular.Value, Muted);
        if (last.HasValue) Text(canvas, Rub(last.Value) + " ₽", x + width - 20, y + 28, 16, Bold.Value, Ink, right: true);
        var values = card.SpendSeries;
        var max = values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(1m).Max();
        if (max <= 0) max = 1;
        var count = values.Count;
        var slot = (width - 40) / count;
        var top = y + 44; var bottom = y + height - 30;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        for (var i = 0; i < count; i++)
        {
            var left = x + 20 + i * slot + slot * 0.15f;
            var barWidth = slot * 0.7f;
            if (values[i] is { } value)
            {
                var barHeight = Math.Max(4f, (float)(value / max) * (bottom - top));
                paint.Color = i == count - 1 ? Orange : Orange.WithAlpha(110);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(left, bottom - barHeight, left + barWidth, bottom), 5), paint);
            }
            Text(canvas, card.Days[i].ToString("dd.MM"), left + barWidth / 2, y + height - 10, 12, Regular.Value, Muted, center: true);
        }
    }

    private static void LinePanel(SKCanvas canvas, float x, float y, float width, float height, string title, TelegramCardData card)
    {
        Box(canvas, x, y, width, height);
        var last = card.DrrSeries.LastOrDefault(v => v.HasValue);
        Text(canvas, title, x + 20, y + 28, 16, Regular.Value, Muted);
        if (last.HasValue) Text(canvas, Percent(last.Value), x + width - 20, y + 28, 16, Bold.Value, Ink, right: true);
        var all = card.DrrSeries.Concat(card.PreviousDrrSeries ?? []).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (all.Count < 2) return;
        var min = all.Min(); var max = all.Max();
        if (max - min < 0.5m) { min -= 0.5m; max += 0.5m; }
        var count = card.DrrSeries.Count;
        var slot = (width - 40) / count;
        var top = y + 48; var bottom = y + height - 34;
        SKPoint? Point(IReadOnlyList<decimal?> series, int i) => series[i] is { } v
            ? new SKPoint(x + 20 + i * slot + slot / 2, bottom - (float)((v - min) / (max - min)) * (bottom - top)) : null;
        if (card.PreviousDrrSeries != null) Polyline(canvas, card.PreviousDrrSeries, Point, Teal.WithAlpha(120), dashed: true);
        Polyline(canvas, card.DrrSeries, Point, Teal, dashed: false);
        if (Point(card.DrrSeries, count - 1) is { } end)
        {
            using var dot = new SKPaint { IsAntialias = true, Color = Teal, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(end, 6, dot);
        }
        for (var i = 0; i < count; i++)
            Text(canvas, card.Days[i].ToString("dd.MM"), x + 20 + i * slot + slot / 2, y + height - 10, 12, Regular.Value, Muted, center: true);
    }

    private static void Polyline(SKCanvas canvas, IReadOnlyList<decimal?> series, Func<IReadOnlyList<decimal?>, int, SKPoint?> point,
        SKColor color, bool dashed)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = dashed ? 3 : 4,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            PathEffect = dashed ? SKPathEffect.CreateDash([8f, 6f], 0) : null
        };
        using var path = new SKPath();
        var open = false;
        for (var i = 0; i < series.Count; i++)
        {
            if (point(series, i) is not { } p) { open = false; continue; }
            if (open) path.LineTo(p); else path.MoveTo(p);
            open = true;
        }
        canvas.DrawPath(path, paint);
    }

    private static void Campaigns(SKCanvas canvas, float x, float y, float width, IReadOnlyList<TelegramSummaryCampaign> rows)
    {
        var height = 50 + rows.Count * 58;
        Box(canvas, x, y, width, height);
        var spendX = x + width - 190; var ordersX = x + width - 105; var drrX = x + width - 20;
        Text(canvas, "Кампания", x + 20, y + 32, 14, Regular.Value, Muted);
        Text(canvas, "Расход", spendX, y + 32, 14, Regular.Value, Muted, right: true);
        Text(canvas, "Заказы", ordersX, y + 32, 14, Regular.Value, Muted, right: true);
        Text(canvas, "ДРР", drrX, y + 32, 14, Regular.Value, Muted, right: true);
        for (var i = 0; i < rows.Count; i++)
        {
            var top = y + 50 + i * 58;
            using (var rule = new SKPaint { Color = Line, StrokeWidth = 1 }) canvas.DrawLine(x + 1, top, x + width - 1, top, rule);
            var row = rows[i];
            Text(canvas, Fit(row.Name, spendX - x - 120, 18, Bold.Value), x + 20, top + 35, 18, Bold.Value, Ink);
            Text(canvas, Rub(row.Spend) + " ₽", spendX, top + 35, 18, Bold.Value, Ink, right: true);
            Text(canvas, row.Orders.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), ordersX, top + 35, 18, Regular.Value, Ink, right: true);
            Text(canvas, row.Drr.HasValue ? Percent(row.Drr.Value) : "—", drrX, top + 35, 18, Regular.Value, Ink, right: true);
        }
    }

    private static void Box(SKCanvas canvas, float x, float y, float width, float height)
    {
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Color = Line, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        var rect = new SKRoundRect(new SKRect(x, y, x + width, y + height), 20);
        canvas.DrawRoundRect(rect, fill);
        canvas.DrawRoundRect(rect, stroke);
    }

    private static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface face, SKColor color,
        bool right = false, bool center = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = new SKPaint { IsAntialias = true, Color = color, TextSize = size, Typeface = face, SubpixelText = true };
        if (right) x -= paint.MeasureText(text);
        else if (center) x -= paint.MeasureText(text) / 2;
        canvas.DrawText(text, x, y, paint);
    }

    private static float Measure(string text, float size, SKTypeface face)
    {
        using var paint = new SKPaint { TextSize = size, Typeface = face };
        return paint.MeasureText(text);
    }

    private static string Fit(string text, float maxWidth, float size, SKTypeface? face = null)
    {
        face ??= Regular.Value;
        if (Measure(text, size, face) <= maxWidth) return text;
        while (text.Length > 1 && Measure(text + "…", size, face) > maxWidth) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    private static string Rub(decimal value) => TelegramSummaryFormatter.FormatRub(value);

    private static string Percent(decimal value) => TelegramSummaryFormatter.FormatDrr(value);
}

using System.Globalization;
using System.IO.Compression;

namespace Ecomads.WebApplication.Services.Telegram;

/// <summary>
/// Draws the "spend and DRR for the last days" chart as a PNG without any graphics library:
/// a tiny software rasterizer (anti-aliased lines, dashes, dots) and a built-in digit font.
/// The image contains only numbers and dates; names and the legend go into the message text,
/// so no font files are needed on the server.
/// </summary>
public static class TelegramChartRenderer
{
    private const int Width = 1040;
    private const int Height = 440;
    private const int Scale = 2; // drawn at 2x and averaged down for smooth edges

    private static readonly (byte R, byte G, byte B) Orange = (242, 140, 0);
    private static readonly (byte R, byte G, byte B) Teal = (20, 163, 154);
    private static readonly (byte R, byte G, byte B) Grid = (226, 230, 236);
    private static readonly (byte R, byte G, byte B) Axis = (120, 128, 140);

    public sealed record Series(IReadOnlyList<decimal?> Values);

    /// <summary>Returns a PNG, or null when there is nothing worth drawing (fewer than two points of spend).</summary>
    public const string Caption = "Расход (оранжевая) и ДРР от заказов (зелёная) за 7 дней. Штриховые линии — предыдущие 7 дней.";

    public static byte[]? Render(IReadOnlyList<DateOnly> days, IReadOnlyList<decimal?> spend, IReadOnlyList<decimal?> drr,
        IReadOnlyList<decimal?>? previousSpend, IReadOnlyList<decimal?>? previousDrr)
    {
        if (days.Count < 2 || spend.Count != days.Count || drr.Count != days.Count) return null;
        if (spend.Count(x => x.HasValue) < 2) return null;
        var canvas = new Canvas(Width * Scale, Height * Scale);
        canvas.Fill(255, 255, 255);

        var left = 130 * Scale; var right = (Width - 120) * Scale;
        var top = 36 * Scale; var bottom = (Height - 64) * Scale;
        var spendAxis = NiceRange(spend.Concat(previousSpend ?? []));
        var drrAxis = NiceRange(drr.Concat(previousDrr ?? []));

        // grid + both value axes
        for (var i = 0; i <= 4; i++)
        {
            var y = bottom - (bottom - top) * i / 4f;
            canvas.Line(left, y, right, y, 1.5f * Scale, Grid, 1f);
            var spendValue = spendAxis.Min + (spendAxis.Max - spendAxis.Min) * i / 4m;
            var drrValue = drrAxis.Min + (drrAxis.Max - drrAxis.Min) * i / 4m;
            canvas.TextRight(left - 12 * Scale, y, FormatSpend(spendValue), Orange, 3 * Scale);
            canvas.TextLeft(right + 12 * Scale, y, FormatPercent(drrValue), Teal, 3 * Scale);
        }
        float X(int index) => left + (right - left) * index / (float)(days.Count - 1);
        float Y(decimal value, (decimal Min, decimal Max) axis) =>
            bottom - (float)((value - axis.Min) / (axis.Max - axis.Min)) * (bottom - top);

        for (var i = 0; i < days.Count; i++)
            canvas.TextCenter(X(i), bottom + 26 * Scale, days[i].ToString("dd.MM", CultureInfo.InvariantCulture), Axis, 3 * Scale);

        // previous period: dashed and lighter, under the current lines
        if (previousSpend is { Count: > 0 } && previousSpend.Count == days.Count)
            Polyline(canvas, previousSpend, X, v => Y(v, spendAxis), Orange, 0.45f, 3f * Scale, dashed: true, dots: false);
        if (previousDrr is { Count: > 0 } && previousDrr.Count == days.Count)
            Polyline(canvas, previousDrr, X, v => Y(v, drrAxis), Teal, 0.45f, 3f * Scale, dashed: true, dots: false);
        Polyline(canvas, spend, X, v => Y(v, spendAxis), Orange, 1f, 4f * Scale, dashed: false, dots: true);
        Polyline(canvas, drr, X, v => Y(v, drrAxis), Teal, 1f, 4f * Scale, dashed: false, dots: true);

        return canvas.ToPng(Scale);
    }

    private static void Polyline(Canvas canvas, IReadOnlyList<decimal?> values, Func<int, float> x, Func<decimal, float> y,
        (byte R, byte G, byte B) color, float alpha, float width, bool dashed, bool dots)
    {
        int? last = null;
        for (var i = 0; i < values.Count; i++)
        {
            if (!values[i].HasValue) { last = null; continue; }
            if (last.HasValue)
            {
                var a = (x(last.Value), y(values[last.Value]!.Value));
                var b = (x(i), y(values[i]!.Value));
                if (dashed) canvas.DashedLine(a.Item1, a.Item2, b.Item1, b.Item2, width, color, alpha, 14 * Scale, 9 * Scale);
                else canvas.Line(a.Item1, a.Item2, b.Item1, b.Item2, width, color, alpha);
            }
            last = i;
        }
        if (!dots) return;
        for (var i = 0; i < values.Count; i++)
            if (values[i].HasValue) canvas.Dot(x(i), y(values[i]!.Value), 6f * Scale, color, alpha);
    }

    /// <summary>Axis range split into four equal steps of a round size (1, 2, 5 x 10^n) that contains all values.</summary>
    internal static (decimal Min, decimal Max) NiceRange(IEnumerable<decimal?> values)
    {
        var list = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (list.Count == 0) return (0m, 4m);
        var min = list.Min(); var max = list.Max();
        if (max == min) { max = min + Math.Max(1m, Math.Abs(min) * 0.1m); min = Math.Max(0m, min - Math.Abs(min) * 0.1m); }
        var rough = (double)((max - min) / 4m);
        var baseMagnitude = Math.Floor(Math.Log10(rough));
        var step = 0m;
        for (var exponent = 0; exponent <= 3 && step == 0m; exponent++)
            foreach (var factor in new[] { 1d, 2d, 2.5d, 5d })
            {
                var candidate = (decimal)(factor * Math.Pow(10, baseMagnitude + exponent));
                var start = Math.Max(0m, Math.Floor(min / candidate) * candidate);
                if (start + 4 * candidate >= max) { step = candidate; break; }
            }
        if (step == 0m) step = (decimal)Math.Pow(10, baseMagnitude + 4);
        var low = Math.Max(0m, Math.Floor(min / step) * step);
        return (low, low + 4 * step);
    }

    internal static string FormatSpend(decimal value)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return rounded.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
    }

    internal static string FormatPercent(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + "%";

    private sealed class Canvas
    {
        private readonly int _w, _h;
        private readonly float[] _pixels; // RGB, 0..255

        public Canvas(int w, int h) { _w = w; _h = h; _pixels = new float[w * h * 3]; }

        public void Fill(byte r, byte g, byte b)
        {
            for (var i = 0; i < _w * _h; i++) { _pixels[i * 3] = r; _pixels[i * 3 + 1] = g; _pixels[i * 3 + 2] = b; }
        }

        private void Blend(int px, int py, (byte R, byte G, byte B) c, float coverage)
        {
            if (px < 0 || py < 0 || px >= _w || py >= _h || coverage <= 0f) return;
            var i = (py * _w + px) * 3;
            _pixels[i] += (c.R - _pixels[i]) * coverage;
            _pixels[i + 1] += (c.G - _pixels[i + 1]) * coverage;
            _pixels[i + 2] += (c.B - _pixels[i + 2]) * coverage;
        }

        public void Line(float x1, float y1, float x2, float y2, float width, (byte R, byte G, byte B) c, float alpha)
        {
            var half = width / 2f;
            var minX = (int)MathF.Floor(MathF.Min(x1, x2) - half - 1); var maxX = (int)MathF.Ceiling(MathF.Max(x1, x2) + half + 1);
            var minY = (int)MathF.Floor(MathF.Min(y1, y2) - half - 1); var maxY = (int)MathF.Ceiling(MathF.Max(y1, y2) + half + 1);
            var dx = x2 - x1; var dy = y2 - y1; var len2 = dx * dx + dy * dy;
            for (var py = minY; py <= maxY; py++)
                for (var px = minX; px <= maxX; px++)
                {
                    var t = len2 == 0 ? 0f : Math.Clamp(((px + .5f - x1) * dx + (py + .5f - y1) * dy) / len2, 0f, 1f);
                    var cx = x1 + t * dx; var cy = y1 + t * dy;
                    var dist = MathF.Sqrt((px + .5f - cx) * (px + .5f - cx) + (py + .5f - cy) * (py + .5f - cy));
                    Blend(px, py, c, Math.Clamp(half + .5f - dist, 0f, 1f) * alpha);
                }
        }

        public void DashedLine(float x1, float y1, float x2, float y2, float width, (byte R, byte G, byte B) c, float alpha, float dash, float gap)
        {
            var length = MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (length == 0) return;
            for (var s = 0f; s < length; s += dash + gap)
            {
                var e = MathF.Min(s + dash, length);
                Line(x1 + (x2 - x1) * s / length, y1 + (y2 - y1) * s / length,
                    x1 + (x2 - x1) * e / length, y1 + (y2 - y1) * e / length, width, c, alpha);
            }
        }

        public void Dot(float cx, float cy, float radius, (byte R, byte G, byte B) c, float alpha)
        {
            for (var py = (int)(cy - radius - 1); py <= (int)(cy + radius + 1); py++)
                for (var px = (int)(cx - radius - 1); px <= (int)(cx + radius + 1); px++)
                {
                    var dist = MathF.Sqrt((px + .5f - cx) * (px + .5f - cx) + (py + .5f - cy) * (py + .5f - cy));
                    Blend(px, py, c, Math.Clamp(radius + .5f - dist, 0f, 1f) * alpha);
                }
        }

        private static int TextWidth(string text, int pixel) => text.Length * 6 * pixel - pixel;

        public void TextLeft(float x, float centerY, string text, (byte R, byte G, byte B) c, int pixel) =>
            DrawText(x, centerY - 3.5f * pixel, text, c, pixel);
        public void TextRight(float x, float centerY, string text, (byte R, byte G, byte B) c, int pixel) =>
            DrawText(x - TextWidth(text, pixel), centerY - 3.5f * pixel, text, c, pixel);
        public void TextCenter(float x, float topY, string text, (byte R, byte G, byte B) c, int pixel) =>
            DrawText(x - TextWidth(text, pixel) / 2f, topY, text, c, pixel);

        private void DrawText(float x, float y, string text, (byte R, byte G, byte B) c, int pixel)
        {
            foreach (var ch in text)
            {
                if (Glyphs.TryGetValue(ch, out var rows))
                    for (var row = 0; row < 7; row++)
                        for (var col = 0; col < 5; col++)
                            if (rows[row][col] == '#')
                                for (var py = 0; py < pixel; py++)
                                    for (var px = 0; px < pixel; px++)
                                        Blend((int)(x + col * pixel + px), (int)(y + row * pixel + py), c, 1f);
                x += 6 * pixel;
            }
        }

        public byte[] ToPng(int scale)
        {
            var w = _w / scale; var h = _h / scale;
            var raw = new byte[(w * 3 + 1) * h];
            for (var y = 0; y < h; y++)
            {
                raw[y * (w * 3 + 1)] = 0; // filter: none
                for (var x = 0; x < w; x++)
                    for (var channel = 0; channel < 3; channel++)
                    {
                        float sum = 0;
                        for (var sy = 0; sy < scale; sy++)
                            for (var sx = 0; sx < scale; sx++)
                                sum += _pixels[(((y * scale + sy) * _w) + x * scale + sx) * 3 + channel];
                        raw[y * (w * 3 + 1) + 1 + x * 3 + channel] = (byte)Math.Clamp((int)MathF.Round(sum / (scale * scale)), 0, 255);
                    }
            }
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw, 0, raw.Length);

            using var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var header = new byte[13];
            WriteInt(header, 0, w); WriteInt(header, 4, h);
            header[8] = 8; header[9] = 2; // 8-bit RGB
            Chunk(png, "IHDR", header);
            Chunk(png, "IDAT", compressed.ToArray());
            Chunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void WriteInt(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24); target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8); target[offset + 3] = (byte)value;
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4]; WriteInt(length, 0, data.Length);
            stream.Write(length);
            var body = new byte[4 + data.Length];
            for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
            data.CopyTo(body, 4);
            stream.Write(body);
            var crc = new byte[4]; WriteInt(crc, 0, unchecked((int)Crc32(body)));
            stream.Write(crc);
        }

        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "],
        ['1'] = ["  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### "],
        ['2'] = [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"],
        ['3'] = [" ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### "],
        ['4'] = ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "],
        ['5'] = ["#####", "#    ", "#### ", "    #", "    #", "#   #", " ### "],
        ['6'] = ["  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### "],
        ['7'] = ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        ['8'] = [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
        ['9'] = [" ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  "],
        ['.'] = ["     ", "     ", "     ", "     ", "     ", " ##  ", " ##  "],
        [','] = ["     ", "     ", "     ", "     ", " ##  ", "  #  ", " #   "],
        ['%'] = ["##  #", "## # ", "  #  ", " #   ", " # ##", "#  ##", "     "],
        [' '] = ["     ", "     ", "     ", "     ", "     ", "     ", "     "],
    };
}

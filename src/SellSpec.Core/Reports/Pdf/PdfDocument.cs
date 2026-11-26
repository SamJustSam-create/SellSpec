using System.Globalization;
using System.Text;

namespace SellSpec.Core.Reports.Pdf;

public enum PdfFont
{
    Helvetica,
    HelveticaBold,
    HelveticaOblique,
}

public readonly record struct PdfColor(double R, double G, double B)
{
    public static PdfColor FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        return new PdfColor(
            int.Parse(h[..2], NumberStyles.HexNumber) / 255d,
            int.Parse(h[2..4], NumberStyles.HexNumber) / 255d,
            int.Parse(h[4..6], NumberStyles.HexNumber) / 255d);
    }

    public static PdfColor Black { get; } = new(0, 0, 0);
    public static PdfColor White { get; } = new(1, 1, 1);
}

/// <summary>
/// A deliberately small PDF 1.4 writer: standard Type 1 fonts (no embedding), vector shapes and WinAnsi text.
/// It exists so SellSpec can export PDFs without pulling in a PDF library and its licence obligations.
/// </summary>
public sealed class PdfDocument
{
    public const double A4Width = 595.28;
    public const double A4Height = 841.89;

    private readonly List<PdfPage> _pages = new();

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage(double width = A4Width, double height = A4Height)
    {
        var page = new PdfPage(width, height);
        _pages.Add(page);
        return page;
    }

    public byte[] Save(string title, string producer, DateTimeOffset created)
    {
        var objects = new List<byte[]>();

        // 1 catalog, 2 pages, 3-5 fonts, 6 info, then page/content pairs.
        var pageObjectNumbers = new List<int>();
        var firstPageObject = 7;
        for (var i = 0; i < _pages.Count; i++)
        {
            pageObjectNumbers.Add(firstPageObject + i * 2);
        }

        objects.Add(Latin1("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Latin1($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {_pages.Count} >>"));
        objects.Add(Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objects.Add(Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        objects.Add(Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>"));
        objects.Add(Latin1($"<< /Title ({PdfText.Escape(title)}) /Producer ({PdfText.Escape(producer)}) /Creator ({PdfText.Escape(producer)}) /CreationDate ({FormatDate(created)}) >>"));

        for (var i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var contentNumber = pageObjectNumbers[i] + 1;
            objects.Add(Latin1(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Fmt(page.Width)} {Fmt(page.Height)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentNumber} 0 R >>"));
            var content = Latin1(page.ContentStream);
            var stream = new List<byte>();
            stream.AddRange(Latin1($"<< /Length {content.Length} >>\nstream\n"));
            stream.AddRange(content);
            stream.AddRange(Latin1("\nendstream"));
            objects.Add(stream.ToArray());
        }

        using var ms = new MemoryStream();
        void Write(byte[] bytes) => ms.Write(bytes, 0, bytes.Length);

        Write(Latin1("%PDF-1.4\n"));
        Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write(Latin1($"{i + 1} 0 obj\n"));
            Write(objects[i]);
            Write(Latin1("\nendobj\n"));
        }

        var xrefPosition = ms.Position;
        Write(Latin1($"xref\n0 {objects.Count + 1}\n"));
        Write(Latin1("0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            Write(Latin1($"{offset:0000000000} 00000 n \n"));
        }
        Write(Latin1($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n"));
        return ms.ToArray();
    }

    private static string FormatDate(DateTimeOffset d)
    {
        var offset = d.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"D:{d:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'";
    }

    internal static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);
}

/// <summary>A single page. Coordinates passed to the drawing methods are top-down (y grows downwards) in points.</summary>
public sealed class PdfPage
{
    private readonly StringBuilder _content = new();

    public double Width { get; }
    public double Height { get; }

    internal PdfPage(double width, double height)
    {
        Width = width;
        Height = height;
    }

    internal string ContentStream => _content.ToString();

    private double Y(double topDownY) => Height - topDownY;

    public void FillRect(double x, double y, double w, double h, PdfColor color)
    {
        _content.Append(Color(color, fill: true))
            .Append($"{F(x)} {F(Y(y + h))} {F(w)} {F(h)} re f\n");
    }

    public void StrokeLine(double x1, double y1, double x2, double y2, double lineWidth, PdfColor color)
    {
        _content.Append(Color(color, fill: false))
            .Append($"{F(lineWidth)} w {F(x1)} {F(Y(y1))} m {F(x2)} {F(Y(y2))} l S\n");
    }

    public void FillCircle(double cx, double cy, double r, PdfColor color)
    {
        const double k = 0.5523;
        var y = Y(cy);
        _content.Append(Color(color, fill: true));
        _content.Append($"{F(cx + r)} {F(y)} m\n");
        _content.Append($"{F(cx + r)} {F(y + r * k)} {F(cx + r * k)} {F(y + r)} {F(cx)} {F(y + r)} c\n");
        _content.Append($"{F(cx - r * k)} {F(y + r)} {F(cx - r)} {F(y + r * k)} {F(cx - r)} {F(y)} c\n");
        _content.Append($"{F(cx - r)} {F(y - r * k)} {F(cx - r * k)} {F(y - r)} {F(cx)} {F(y - r)} c\n");
        _content.Append($"{F(cx + r * k)} {F(y - r)} {F(cx + r)} {F(y - r * k)} {F(cx + r)} {F(y)} c f\n");
    }

    /// <summary>Draws a single line of text with its baseline at <paramref name="baselineY"/> (top-down).</summary>
    public void Text(double x, double baselineY, string text, PdfFont font, double size, PdfColor color)
    {
        if (string.IsNullOrEmpty(text)) return;
        _content.Append(Color(color, fill: true))
            .Append($"BT /{FontName(font)} {F(size)} Tf {F(x)} {F(Y(baselineY))} Td ({PdfText.Escape(PdfText.ToWinAnsi(text))}) Tj ET\n");
    }

    public void TextRight(double rightX, double baselineY, string text, PdfFont font, double size, PdfColor color)
    {
        var w = PdfMetrics.Width(text, font, size);
        Text(rightX - w, baselineY, text, font, size, color);
    }

    private static string FontName(PdfFont font) => font switch
    {
        PdfFont.HelveticaBold => "F2",
        PdfFont.HelveticaOblique => "F3",
        _ => "F1",
    };

    private static string Color(PdfColor c, bool fill) =>
        $"{F(c.R)} {F(c.G)} {F(c.B)} {(fill ? "rg" : "RG")}\n";

    private static string F(double v) => PdfDocument.Fmt(v);
}

public static class PdfText
{
    /// <summary>Escapes a WinAnsi string for a PDF literal string.</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32 || ch > 255) sb.Append('?');
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Maps common Unicode punctuation to WinAnsi code points; everything else outside Latin-1 becomes '?'.</summary>
    public static string ToWinAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '€' => (char)0x80, // euro
                '…' => (char)0x85, // ellipsis
                '–' => (char)0x96, // en dash
                '—' => (char)0x97, // em dash
                '‘' => (char)0x91,
                '’' => (char)0x92,
                '“' => (char)0x93,
                '”' => (char)0x94,
                '•' => (char)0x95, // bullet
                '™' => (char)0x99, // trademark
                '×' => (char)0xD7, // multiply (already Latin-1)
                '·' => (char)0xB7, // middle dot
                '°' => (char)0xB0,
                '®' => (char)0xAE,
                '©' => (char)0xA9,
                '→' => '>',
                '✓' or '✔' => (char)0xFC, // no tick in WinAnsi; use 'ü'? no – fall back to text label instead
                _ => ch <= 0xFF ? ch : '?',
            });
        }
        // Replace the tick placeholder with a plain character the reader expects.
        return sb.ToString().Replace((char)0xFC + string.Empty, "v");
    }
}

/// <summary>Advance widths (per 1000 em) for Helvetica and Helvetica-Bold in WinAnsi encoding.</summary>
public static class PdfMetrics
{
    private static readonly int[] Helvetica =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 48-63
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // 64-79
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // 80-95
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // 96-111
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, // 112-126
    };

    private static readonly int[] HelveticaBold =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    public static double Width(string text, PdfFont font, double size)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var table = font == PdfFont.HelveticaBold ? HelveticaBold : Helvetica;
        double total = 0;
        foreach (var raw in PdfText.ToWinAnsi(text))
        {
            int w;
            var ch = raw;
            if (ch >= 32 && ch <= 126)
            {
                w = table[ch - 32];
            }
            else
            {
                w = ch switch
                {
                    (char)0x96 => 556, // en dash
                    (char)0x97 => 1000, // em dash
                    (char)0x95 => 350, // bullet
                    (char)0x85 => 1000, // ellipsis
                    (char)0x91 or (char)0x92 => font == PdfFont.HelveticaBold ? 278 : 222,
                    (char)0x93 or (char)0x94 => font == PdfFont.HelveticaBold ? 500 : 333,
                    (char)0xB7 => 278,
                    (char)0xB0 => 400,
                    (char)0xD7 => 584,
                    (char)0xAE or (char)0xA9 => 737,
                    (char)0x99 => 1000,
                    (char)0x80 => 556,
                    _ => 556,
                };
            }
            total += w;
        }
        return total * size / 1000d;
    }

    /// <summary>Greedy word wrap. Words longer than the width are broken by character.</summary>
    public static IReadOnlyList<string> Wrap(string text, PdfFont font, double size, double maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Width(candidate, font, size) <= maxWidth)
                {
                    current.Clear().Append(candidate);
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                if (Width(word, font, size) <= maxWidth)
                {
                    current.Append(word);
                }
                else
                {
                    var piece = new StringBuilder();
                    foreach (var ch in word)
                    {
                        if (Width(piece.ToString() + ch, font, size) > maxWidth && piece.Length > 0)
                        {
                            lines.Add(piece.ToString());
                            piece.Clear();
                        }
                        piece.Append(ch);
                    }
                    current.Append(piece);
                }
            }
            lines.Add(current.ToString());
        }
        return lines;
    }
}

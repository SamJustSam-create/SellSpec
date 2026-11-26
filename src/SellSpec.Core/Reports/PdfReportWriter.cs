using SellSpec.Core.Models;
using SellSpec.Core.Reports.Pdf;

namespace SellSpec.Core.Reports;

/// <summary>Lays a <see cref="DeviceReport"/> out as a neutral, professional A4 PDF using the built-in writer.</summary>
public static class PdfReportWriter
{
    private const double Margin = 44;
    private const double LabelColumn = 150;
    private static readonly PdfColor Ink = PdfColor.FromHex("1B1B1B");
    private static readonly PdfColor Muted = PdfColor.FromHex("5C5C5C");
    private static readonly PdfColor Faint = PdfColor.FromHex("8A8A8A");
    private static readonly PdfColor Rule = PdfColor.FromHex("D9D9D9");
    private static readonly PdfColor Band = PdfColor.FromHex("1F2A37");
    private static readonly PdfColor Accent = PdfColor.FromHex("0F6CBD");
    private static readonly PdfColor Good = PdfColor.FromHex("0F7B0F");
    private static readonly PdfColor Attention = PdfColor.FromHex("B5650A");
    private static readonly PdfColor Issue = PdfColor.FromHex("C42B1C");
    private static readonly PdfColor Grey = PdfColor.FromHex("8A8A8A");
    private static readonly PdfColor Notice = PdfColor.FromHex("FFF4CE");

    public static byte[] Write(DeviceReport report)
    {
        var doc = new PdfDocument();
        var layout = new Layout(doc, report);

        layout.Header();
        layout.Title();
        foreach (var notice in report.Notices)
        {
            layout.NoticeBox(notice);
        }

        layout.SectionTitle("Specifications");
        foreach (var section in report.Specifications)
        {
            layout.SubsectionTitle(section.Title);
            foreach (var field in section.Fields)
            {
                layout.FieldRow(field);
            }
        }

        layout.SectionTitle("Device health");
        foreach (var row in report.Health)
        {
            layout.StatusRow(row);
        }

        layout.SectionTitle("Hardware tests");
        foreach (var row in report.HardwareTests)
        {
            layout.StatusRow(row);
        }

        layout.SectionTitle("Seller-reported condition");
        foreach (var field in report.SellerCondition)
        {
            layout.FieldRow(field);
        }

        if (report.KnownIssues.Count > 0)
        {
            layout.SectionTitle("Known issues");
            foreach (var issue in report.KnownIssues)
            {
                layout.Bullet(issue);
            }
        }

        layout.Footers();
        return doc.Save($"SellSpec device report – {report.DeviceName}", $"SellSpec {report.ApplicationVersion}", report.GeneratedAt);
    }

    private sealed class Layout
    {
        private readonly PdfDocument _doc;
        private readonly DeviceReport _report;
        private PdfPage _page;
        private double _y;
        private readonly double _contentWidth;
        private const double FooterHeight = 58;

        public Layout(PdfDocument doc, DeviceReport report)
        {
            _doc = doc;
            _report = report;
            _page = doc.AddPage();
            _contentWidth = _page.Width - Margin * 2;
            _y = Margin;
        }

        private double Bottom => _page.Height - Margin - FooterHeight;

        private void Ensure(double needed)
        {
            if (_y + needed <= Bottom) return;
            _page = _doc.AddPage();
            _y = Margin;
            // Small running header on continuation pages.
            _page.Text(Margin, _y + 8, "SellSpec device report", PdfFont.HelveticaBold, 9, Muted);
            _page.TextRight(_page.Width - Margin, _y + 8, _report.DeviceName, PdfFont.Helvetica, 9, Muted);
            _y += 16;
            _page.StrokeLine(Margin, _y, _page.Width - Margin, _y, 0.5, Rule);
            _y += 14;
        }

        public void Header()
        {
            const double bandHeight = 62;
            _page.FillRect(0, 0, _page.Width, bandHeight, Band);
            _page.FillRect(0, bandHeight, _page.Width, 3, Accent);
            _page.Text(Margin, 30, "SellSpec", PdfFont.HelveticaBold, 20, PdfColor.White);
            _page.Text(Margin, 46, "Device specification report", PdfFont.Helvetica, 10, PdfColor.FromHex("C9D3E0"));
            _page.TextRight(_page.Width - Margin, 30, $"Scanned {_report.ScannedAt:d MMMM yyyy}", PdfFont.Helvetica, 9.5, PdfColor.White);
            _page.TextRight(_page.Width - Margin, 46, $"SellSpec {_report.ApplicationVersion}", PdfFont.Helvetica, 9, PdfColor.FromHex("C9D3E0"));
            _y = bandHeight + 3 + 28;
        }

        public void Title()
        {
            var nameLines = PdfMetrics.Wrap(_report.DeviceName, PdfFont.HelveticaBold, 17, _contentWidth);
            foreach (var line in nameLines)
            {
                _page.Text(Margin, _y, line, PdfFont.HelveticaBold, 17, Ink);
                _y += 21;
            }
            var sub = string.Join(" · ", new[] { _report.DeviceSecondaryName, _report.DeviceType, _report.OperatingSystem }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (sub.Length > 0)
            {
                _page.Text(Margin, _y, sub, PdfFont.Helvetica, 10.5, Muted);
                _y += 15;
            }
            if (!string.IsNullOrWhiteSpace(_report.Headline))
            {
                foreach (var line in PdfMetrics.Wrap(_report.Headline, PdfFont.Helvetica, 10.5, _contentWidth))
                {
                    _page.Text(Margin, _y, line, PdfFont.Helvetica, 10.5, Ink);
                    _y += 14;
                }
            }
            _y += 6;
        }

        public void NoticeBox(string text)
        {
            var lines = PdfMetrics.Wrap(text, PdfFont.Helvetica, 9.5, _contentWidth - 24);
            var height = lines.Count * 13 + 14;
            Ensure(height + 8);
            _page.FillRect(Margin, _y, _contentWidth, height, Notice);
            _page.FillRect(Margin, _y, 3, height, Attention);
            var ty = _y + 16;
            foreach (var line in lines)
            {
                _page.Text(Margin + 12, ty, line, PdfFont.Helvetica, 9.5, Ink);
                ty += 13;
            }
            _y += height + 10;
        }

        public void SectionTitle(string title)
        {
            Ensure(40);
            _y += 10;
            _page.Text(Margin, _y + 4, title.ToUpperInvariant(), PdfFont.HelveticaBold, 10, Accent);
            _y += 10;
            _page.StrokeLine(Margin, _y, _page.Width - Margin, _y, 0.75, Rule);
            _y += 14;
        }

        public void SubsectionTitle(string title)
        {
            Ensure(34);
            _y += 10;
            _page.Text(Margin, _y + 4, title, PdfFont.HelveticaBold, 10.5, Ink);
            _y += 16;
        }

        public void FieldRow(ReportField field)
        {
            var value = field.Value;
            var valueWidth = _contentWidth - LabelColumn;
            var valueLines = PdfMetrics.Wrap(value, PdfFont.Helvetica, 9.5, valueWidth);
            var tag = field.ProvenanceTag;
            var noteLines = string.IsNullOrWhiteSpace(field.Note) ? Array.Empty<string>() : PdfMetrics.Wrap(field.Note!, PdfFont.HelveticaOblique, 8, valueWidth).ToArray();
            var height = valueLines.Count * 12 + noteLines.Length * 10 + 5;
            Ensure(height);

            _page.Text(Margin, _y + 9, field.Label, PdfFont.Helvetica, 9, Muted);
            var ty = _y + 9;
            for (var i = 0; i < valueLines.Count; i++)
            {
                var line = valueLines[i];
                var colour = field.Provenance is Provenance.Unavailable or Provenance.NotApplicable ? Faint : Ink;
                _page.Text(Margin + LabelColumn, ty, line, PdfFont.Helvetica, 9.5, colour);
                if (i == valueLines.Count - 1 && tag is not null && tag is not ("unavailable" or "not applicable"))
                {
                    var w = PdfMetrics.Width(line, PdfFont.Helvetica, 9.5);
                    var tagText = $"({tag})";
                    if (Margin + LabelColumn + w + 6 + PdfMetrics.Width(tagText, PdfFont.HelveticaOblique, 8) <= _page.Width - Margin)
                    {
                        _page.Text(Margin + LabelColumn + w + 6, ty, tagText, PdfFont.HelveticaOblique, 8, Faint);
                    }
                    else
                    {
                        ty += 11;
                        _page.Text(Margin + LabelColumn, ty, tagText, PdfFont.HelveticaOblique, 8, Faint);
                    }
                }
                ty += 12;
            }
            foreach (var note in noteLines)
            {
                _page.Text(Margin + LabelColumn, ty - 2, note, PdfFont.HelveticaOblique, 8, Faint);
                ty += 10;
            }
            _y = ty - 4;
        }

        public void StatusRow(ReportStatusRow row)
        {
            var valueWidth = _contentWidth - LabelColumn - 90;
            var valueLines = PdfMetrics.Wrap(row.Value, PdfFont.Helvetica, 9.5, valueWidth);
            var detailLines = string.IsNullOrWhiteSpace(row.Detail) ? Array.Empty<string>() : PdfMetrics.Wrap(row.Detail!, PdfFont.HelveticaOblique, 8, _contentWidth - LabelColumn).ToArray();
            var height = valueLines.Count * 12 + detailLines.Length * 10 + 6;
            Ensure(height);

            var colour = row.Status switch
            {
                HealthStatusKind.Good => Good,
                HealthStatusKind.Attention => Attention,
                HealthStatusKind.Issue => Issue,
                _ => Grey,
            };
            _page.FillCircle(Margin + 4, _y + 5.5, 3.2, colour);
            _page.Text(Margin + 12, _y + 9, row.Title, PdfFont.Helvetica, 9, Ink);
            _page.Text(Margin + LabelColumn, _y + 9, row.StatusLabel, PdfFont.HelveticaBold, 9, colour);
            var ty = _y + 9;
            foreach (var line in valueLines)
            {
                _page.Text(Margin + LabelColumn + 90, ty, line, PdfFont.Helvetica, 9.5, row.Status is HealthStatusKind.Unknown or HealthStatusKind.NotApplicable ? Faint : Ink);
                ty += 12;
            }
            foreach (var line in detailLines)
            {
                _page.Text(Margin + LabelColumn, ty - 2, line, PdfFont.HelveticaOblique, 8, Faint);
                ty += 10;
            }
            _y = ty - 3;
        }

        public void Bullet(string text)
        {
            var lines = PdfMetrics.Wrap(text, PdfFont.Helvetica, 9.5, _contentWidth - 14);
            Ensure(lines.Count * 12 + 4);
            _page.Text(Margin, _y + 9, "•", PdfFont.Helvetica, 9.5, Ink);
            var ty = _y + 9;
            foreach (var line in lines)
            {
                _page.Text(Margin + 14, ty, line, PdfFont.Helvetica, 9.5, Ink);
                ty += 12;
            }
            _y = ty - 3;
        }

        public void Footers()
        {
            var pages = _doc.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var top = page.Height - Margin - FooterHeight + 10;
                page.StrokeLine(Margin, top, page.Width - Margin, top, 0.5, Rule);
                var ty = top + 11;
                foreach (var line in PdfMetrics.Wrap(_report.Disclaimer, PdfFont.Helvetica, 7, _contentWidth - 60))
                {
                    page.Text(Margin, ty, line, PdfFont.Helvetica, 7, Faint);
                    ty += 8.5;
                }
                page.TextRight(page.Width - Margin, top + 11, $"Page {i + 1} of {pages.Count}", PdfFont.Helvetica, 8, Muted);
                page.TextRight(page.Width - Margin, page.Height - Margin + 6, $"Generated {_report.GeneratedAt:d MMM yyyy, h:mm tt} · SellSpec – Scan it. List it. Sell it.", PdfFont.Helvetica, 7, Faint);
            }
        }
    }
}

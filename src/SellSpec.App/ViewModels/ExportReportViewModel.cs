using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.App.Services;
using SellSpec.Core.Privacy;
using SellSpec.Core.Reports;

namespace SellSpec.App.ViewModels;

public sealed partial class ExportReportViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public ExportReportViewModel(ShellViewModel shell)
    {
        _shell = shell;
        shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSession.ScanResult) or nameof(AppSession.Settings)) Refresh();
        };
        shell.Session.Questionnaire.Changed += (_, _) => Refresh();
        shell.Session.Tests.Changed += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty] private DeviceReport? _report;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _lastExportPath;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Set by the view: renders the preview control to PNG bytes.</summary>
    public Func<DeviceReport, byte[]>? PngRenderer { get; set; }

    public bool HasReport => Report is not null;

    private void Refresh()
    {
        var s = _shell.Session.Snapshot;
        if (s is null)
        {
            Report = null;
            OnPropertyChanged(nameof(HasReport));
            return;
        }
        Report = ReportBuilder.Build(new ReportRequest(
            s,
            _shell.Session.Condition,
            _shell.Session.Tests.Results,
            _shell.Session.ScannedAt,
            AppInfo.Version,
            _shell.Session.IsDemo,
            _shell.Session.Settings.CurrencySymbol));
        OnPropertyChanged(nameof(HasReport));
    }

    private string SuggestedName(string suffix) => $"SellSpec report - {_shell.Session.Snapshot?.Identity.DisplayName}{suffix}";

    [RelayCommand]
    private void ExportPdf() => Export(".pdf", "PDF document (*.pdf)|*.pdf", r => PdfReportWriter.Write(r), r => TextReportWriter.Write(r));

    [RelayCommand]
    private void ExportText() => Export(".txt", "Text files (*.txt)|*.txt", r => System.Text.Encoding.UTF8.GetBytes(TextReportWriter.Write(r)), r => TextReportWriter.Write(r));

    [RelayCommand]
    private void ExportJson() => Export(".json", "JSON files (*.json)|*.json", r => System.Text.Encoding.UTF8.GetBytes(JsonReportWriter.Write(r)), r => JsonReportWriter.Write(r));

    [RelayCommand]
    private void ExportPng()
    {
        if (PngRenderer is null)
        {
            StatusMessage = "The image renderer is not ready yet.";
            return;
        }
        Export(".png", "PNG image (*.png)|*.png", r => PngRenderer(r), r => TextReportWriter.Write(r));
    }

    private void Export(string extension, string filter, Func<DeviceReport, byte[]> produce, Func<DeviceReport, string> textForRedaction)
    {
        var s = _shell.Session.Snapshot;
        if (Report is null || s is null) return;
        var path = _shell.Export.AskForPath(SuggestedName(string.Empty), filter, extension);
        if (path is null) return;

        IsBusy = true;
        try
        {
            var report = Report with { GeneratedAt = DateTimeOffset.Now };
            var bytes = produce(report);
            _shell.Export.WriteBytes(path, bytes, textForRedaction(report), s.Private);
            _shell.RememberExportFolder();
            LastExportPath = path;
            StatusMessage = $"Saved {System.IO.Path.GetFileName(path)} ({bytes.Length / 1024.0:0.#} KB).";
        }
        catch (RedactionException ex)
        {
            StatusMessage = "Not saved: the report contained " + string.Join(", ", ex.Findings) + ". This should not happen; please report it.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusMessage = "The file could not be saved to that location.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RevealLastExport()
    {
        if (LastExportPath is not null) ExportService.RevealInExplorer(LastExportPath);
    }

    [RelayCommand]
    private void CopyText()
    {
        if (Report is null) return;
        StatusMessage = ClipboardService.TrySetText(TextReportWriter.Write(Report)) ? "Report text copied." : "The clipboard is busy. Try again in a moment.";
    }
}

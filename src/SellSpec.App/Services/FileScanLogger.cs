using System.IO;
using SellSpec.Core.Diagnostics;

namespace SellSpec.App.Services;

/// <summary>Appends sanitised log lines to a daily file and keeps only the last seven days.</summary>
public sealed class FileScanLogger : IScanLogger, IDisposable
{
    private readonly string _directory;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _currentFile;

    public FileScanLogger(string directory, bool enabled)
    {
        _directory = directory;
        Enabled = enabled;
        if (enabled)
        {
            TrimOldLogs();
        }
    }

    public bool Enabled { get; set; }

    public string Directory => _directory;

    public string? CurrentFile => _currentFile;

    public void Log(LogLevel level, string component, string message, Exception? exception = null)
    {
        if (!Enabled) return;
        var line = LogSanitiser.FormatLine(level, component, message, exception);
        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer?.WriteLine(line);
                _writer?.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the application.
            }
        }
    }

    private void EnsureWriter()
    {
        var file = Path.Combine(_directory, $"sellspec-{DateTime.Now:yyyyMMdd}.log");
        if (_writer is not null && file == _currentFile) return;
        _writer?.Dispose();
        System.IO.Directory.CreateDirectory(_directory);
        _writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
        _currentFile = file;
    }

    private void TrimOldLogs()
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory)) return;
            foreach (var file in System.IO.Directory.GetFiles(_directory, "sellspec-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

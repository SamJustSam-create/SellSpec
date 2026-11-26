using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using Microsoft.Win32;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Privacy;

namespace SellSpec.App.Services;

public static class ClipboardService
{
    /// <summary>Copies text, retrying briefly because another process can hold the clipboard open.</summary>
    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }
}

public sealed class ExportService
{
    private readonly IScanLogger _logger;

    public ExportService(IScanLogger logger)
    {
        _logger = logger;
    }

    public string? LastDirectory { get; set; }

    /// <summary>Shows a save dialog and returns the chosen full path, or null when cancelled.</summary>
    public string? AskForPath(string suggestedName, string filter, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            FileName = MakeSafeFileName(suggestedName),
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = LastDirectory is { } d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return null;
        var path = Path.GetFullPath(dialog.FileName);
        LastDirectory = Path.GetDirectoryName(path);
        return path;
    }

    /// <summary>Writes text after a final redaction check. Throws <see cref="RedactionException"/> rather than leaking.</summary>
    public void WriteText(string path, string content, Core.Models.PrivateIdentifiers privateIds)
    {
        Redactor.EnsureClean(content, privateIds);
        WriteAtomically(path, System.Text.Encoding.UTF8.GetBytes(content));
        _logger.Log(LogLevel.Info, "Export", $"Wrote text export ({content.Length} chars).");
    }

    public void WriteBytes(string path, byte[] content, string? textForRedactionCheck, Core.Models.PrivateIdentifiers privateIds)
    {
        if (textForRedactionCheck is not null)
        {
            Redactor.EnsureClean(textForRedactionCheck, privateIds);
        }
        WriteAtomically(path, content);
        _logger.Log(LogLevel.Info, "Export", $"Wrote binary export ({content.Length} bytes).");
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) throw new IOException("The export location is not valid.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        }
    }

    public static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        cleaned = cleaned.Replace("–", "-").Replace("  ", " ");
        if (cleaned.Length > 80) cleaned = cleaned[..80].Trim();
        return cleaned.Length == 0 ? "SellSpec report" : cleaned;
    }

    public static void RevealInExplorer(string path)
    {
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }
}

/// <summary>Opens a fixed set of Windows URIs and folders. Nothing user-supplied is ever passed to the shell.</summary>
public static class ExternalLauncher
{
    private static readonly string[] AllowedPrefixes = { "ms-settings:", "microsoft.windows.camera:" };

    public static bool OpenUri(string uri)
    {
        if (!AllowedPrefixes.Any(p => uri.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    public static bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Relaunches SellSpec with administrator rights so storage reliability counters can be read. Triggers a UAC prompt.</summary>
    public static bool RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--elevated-rescan") { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // User declined the UAC prompt.
        }
    }
}

public sealed class SoundService
{
    private SoundPlayer? _player;

    public void Play(byte[] wav)
    {
        Stop();
        _player = new SoundPlayer(new MemoryStream(wav));
        _player.Play();
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }
}

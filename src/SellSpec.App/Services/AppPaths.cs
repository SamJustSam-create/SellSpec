using System.IO;
using System.Reflection;

namespace SellSpec.App.Services;

/// <summary>Where SellSpec keeps its few files. Portable mode keeps everything beside the executable.</summary>
public sealed class AppPaths
{
    public AppPaths(bool forcePortable)
    {
        ExecutableDirectory = AppContext.BaseDirectory;
        var portableMarker = Path.Combine(ExecutableDirectory, "portable.txt");
        IsPortable = forcePortable || File.Exists(portableMarker);

        DataDirectory = IsPortable
            ? Path.Combine(ExecutableDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SellSpec");

        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        AnswersFile = Path.Combine(DataDirectory, "answers.json");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        TempDirectory = Path.Combine(Path.GetTempPath(), "SellSpec");
    }

    public string ExecutableDirectory { get; }
    public bool IsPortable { get; }
    public string DataDirectory { get; }
    public string SettingsFile { get; }

    /// <summary>Remembered questionnaire answers and test results per device model (no identifiers).</summary>
    public string AnswersFile { get; }

    public string LogDirectory { get; }
    public string TempDirectory { get; }

    public string DefaultExportDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Removes leftovers from earlier sessions (e.g. preview files). Never touches anything outside the SellSpec temp folder.</summary>
    public void CleanTemporaryFiles()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }
}

public static class AppInfo
{
    public static string Version { get; } = ComputeVersion();
    public const string ProductName = "SellSpec";
    public const string Tagline = "Scan it. List it. Sell it.";

    private static string ComputeVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the source revision suffix ("+abc123") that the SDK appends.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}

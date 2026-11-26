using System.Text.Json;
using System.Text.Json.Serialization;
using SellSpec.Core.Listing;

namespace SellSpec.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences. Stored as JSON in the SellSpec data folder; nothing here identifies the user.</summary>
public sealed record AppSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;
    public Marketplace DefaultMarketplace { get; init; } = Marketplace.General;
    public int EbayTitleLimit { get; init; } = 80;
    public int FacebookTitleLimit { get; init; } = 99;
    public int GumtreeTitleLimit { get; init; } = 64;
    public bool IncludeSellSpecFooter { get; init; } = true;
    public bool IncludeBatteryHealthInListing { get; init; } = true;
    public bool IncludeWindowsActivationInListing { get; init; } = true;
    public string CurrencySymbol { get; init; } = "$";
    public bool EnableDiagnosticLogging { get; init; } = true;
    public bool ShowProvenanceLabels { get; init; } = true;

    /// <summary>Keep questionnaire answers and hardware test results per device model between sessions.</summary>
    public bool RememberAnswers { get; init; } = true;

    public string? LastExportFolder { get; init; }

    public ListingOptions ToListingOptions() => new()
    {
        EbayTitleLimit = Math.Clamp(EbayTitleLimit, 20, 200),
        FacebookTitleLimit = Math.Clamp(FacebookTitleLimit, 20, 200),
        GumtreeTitleLimit = Math.Clamp(GumtreeTitleLimit, 20, 200),
        IncludeSellSpecFooter = IncludeSellSpecFooter,
        IncludeBatteryHealth = IncludeBatteryHealthInListing,
        IncludeWindowsActivation = IncludeWindowsActivationInListing,
        CurrencySymbol = string.IsNullOrWhiteSpace(CurrencySymbol) ? "$" : CurrencySymbol.Trim(),
    };

    public static AppSettings Default { get; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>Simple JSON file store. Corrupt or unreadable files fall back to defaults rather than crashing the app.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return AppSettings.Default;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        AtomicFile.WriteAllText(_path, json);
    }
}

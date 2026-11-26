using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Testing;

namespace SellSpec.Core.Sessions;

/// <summary>
/// The key under which a device's questionnaire answers and hardware test results are remembered. It is built
/// from public, buyer-facing identity only (brand, product name, model number; the motherboard for a custom
/// build), never from serial numbers or the system UUID. Two identical models therefore share one key, and the
/// saved file never identifies a particular unit.
/// </summary>
public static partial class DeviceKey
{
    public static string? For(DeviceSnapshot snapshot) => For(snapshot.Identity);

    /// <summary>Returns null when the device cannot be identified well enough to remember anything about it.</summary>
    public static string? For(DeviceIdentity identity)
    {
        if (identity.IsVirtualMachine) return null;

        if (identity.IsCustomBuilt)
        {
            // A custom build has no meaningful system model; the board is what the seller would recognise.
            return identity.BoardModel.HasValue ? Join("custom", Text(identity.BoardManufacturer), Text(identity.BoardModel)) : null;
        }

        var product = Text(identity.ProductName);
        var model = Text(identity.ModelNumber);
        if (product is null && model is null) return null; // A brand alone ("HP") is not a model.
        return Join(Text(identity.Manufacturer), product, model);
    }

    private static string? Text(Spec<string> spec) => spec.HasValue ? spec.Value : null;

    private static string Join(params string?[] parts) =>
        string.Join('|', parts.Select(p => p is null ? "-" : Whitespace().Replace(p.Trim().ToLowerInvariant(), " ")));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>What SellSpec remembers for one device model between sessions. Contains no identifiers.</summary>
public sealed record SavedDeviceAnswers
{
    public string DeviceKey { get; init; } = string.Empty;

    /// <summary>Buyer-facing device name at the time of saving, shown when the answers are offered back.</summary>
    public string DeviceLabel { get; init; } = string.Empty;

    public DateTimeOffset SavedAt { get; init; }
    public string? AppVersion { get; init; }
    public ConditionQuestionnaire Questionnaire { get; init; } = ConditionQuestionnaire.Empty;
    public IReadOnlyList<HardwareTestResult> Tests { get; init; } = Array.Empty<HardwareTestResult>();

    [JsonIgnore]
    public bool IsEmpty => Questionnaire.IsEmpty && Tests.Count == 0;
}

public sealed record SavedAnswersFile
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<SavedDeviceAnswers> Devices { get; init; } = Array.Empty<SavedDeviceAnswers>();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SavedAnswersFile))]
internal sealed partial class SavedAnswersJsonContext : JsonSerializerContext
{
}

public interface ISavedAnswersStore
{
    SavedDeviceAnswers? Find(string deviceKey);
    IReadOnlyList<SavedDeviceAnswers> All();

    /// <summary>
    /// Remembers the entry (replacing any earlier one for the same key). An empty entry removes the device instead.
    /// Throws <see cref="RedactionException"/>, writing nothing, if the entry would put a private identifier on disk.
    /// </summary>
    void Save(SavedDeviceAnswers entry, PrivateIdentifiers privateIdentifiers);

    void Remove(string deviceKey);
    void Clear();
}

/// <summary>
/// JSON file in the SellSpec data folder, newest device first and capped so a refurbisher's history stays small.
/// Unreadable or corrupt files read as empty rather than failing the app; every write is redaction-checked and atomic.
/// </summary>
public sealed class JsonSavedAnswersStore : ISavedAnswersStore
{
    public const int MaxDevices = 25;

    private readonly string _path;

    public JsonSavedAnswersStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public SavedDeviceAnswers? Find(string deviceKey) =>
        Load().Devices.FirstOrDefault(d => string.Equals(d.DeviceKey, deviceKey, StringComparison.Ordinal));

    public IReadOnlyList<SavedDeviceAnswers> All() => Load().Devices;

    public void Save(SavedDeviceAnswers entry, PrivateIdentifiers privateIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(entry.DeviceKey)) throw new ArgumentException("A device key is required.", nameof(entry));
        if (entry.IsEmpty)
        {
            Remove(entry.DeviceKey);
            return;
        }

        var stamped = entry with { SavedAt = entry.SavedAt == default ? DateTimeOffset.Now : entry.SavedAt };
        var others = Load().Devices.Where(d => !string.Equals(d.DeviceKey, entry.DeviceKey, StringComparison.Ordinal));
        var devices = new[] { stamped }.Concat(others).Take(MaxDevices).ToList();
        Write(new SavedAnswersFile { Devices = devices }, privateIdentifiers);
    }

    public void Remove(string deviceKey)
    {
        var file = Load();
        var remaining = file.Devices.Where(d => !string.Equals(d.DeviceKey, deviceKey, StringComparison.Ordinal)).ToList();
        if (remaining.Count == file.Devices.Count) return;
        if (remaining.Count == 0)
        {
            Clear();
            return;
        }
        Write(new SavedAnswersFile { Devices = remaining }, PrivateIdentifiers.Empty);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the next Save overwrites the file anyway.
        }
    }

    private SavedAnswersFile Load()
    {
        try
        {
            if (!File.Exists(_path)) return new SavedAnswersFile();
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize(json, SavedAnswersJsonContext.Default.SavedAnswersFile);
            return file is null || file.Devices is null ? new SavedAnswersFile() : file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SavedAnswersFile();
        }
    }

    private void Write(SavedAnswersFile file, PrivateIdentifiers privateIdentifiers)
    {
        var json = JsonSerializer.Serialize(file, SavedAnswersJsonContext.Default.SavedAnswersFile);
        // Same guard as every export: a serial number typed into a note must never reach the disk.
        Redactor.EnsureClean(json, privateIdentifiers);

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }
}

public enum SavedAnswersAction
{
    /// <summary>Nothing saved for this device, or the seller has already entered answers this session.</summary>
    None,

    /// <summary>Show the saved answers and let the seller choose to restore or discard them.</summary>
    Offer,

    /// <summary>Apply the saved answers straight away (the elevated rescan continues the same session).</summary>
    Restore,
}

/// <summary>The small set of rules that decide when remembered answers come back. Pure, so it is unit-tested.</summary>
public static class SavedAnswersPolicy
{
    public static bool CanRemember(bool rememberAnswers, bool isDemo, string? deviceKey) =>
        rememberAnswers && !isDemo && deviceKey is not null;

    public static SavedAnswersAction Decide(SavedDeviceAnswers? saved, bool sessionHasAnswers, bool isElevatedRescan)
    {
        if (saved is null || saved.IsEmpty) return SavedAnswersAction.None;
        if (sessionHasAnswers) return SavedAnswersAction.None; // Never overwrite what the seller entered this session.
        return isElevatedRescan ? SavedAnswersAction.Restore : SavedAnswersAction.Offer;
    }

    /// <summary>A rescan keeps the current answers and test results only while it is still the same device.</summary>
    public static bool KeepSessionAcrossRescan(string? previousKey, string? newKey) =>
        previousKey is not null && newKey is not null && string.Equals(previousKey, newKey, StringComparison.Ordinal);
}

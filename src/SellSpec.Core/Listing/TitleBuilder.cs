using SellSpec.Core.Models;

namespace SellSpec.Core.Listing;

/// <summary>
/// Builds a listing title from the most informative to the most compact candidate and picks the first one
/// that fits the platform's character limit. Truncation is the last resort and happens at a word boundary.
/// </summary>
public static class TitleBuilder
{
    public const string Separator = " – "; // en dash

    public sealed record Result(string Title, bool WasShortened, bool WasTruncated);

    public static Result Build(DeviceSnapshot snapshot, int? maxLength)
    {
        var candidates = Candidates(snapshot).Distinct().ToList();
        if (candidates.Count == 0)
        {
            candidates.Add("Windows computer");
        }

        if (maxLength is null || maxLength <= 0)
        {
            return new Result(candidates[0], false, false);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Length <= maxLength)
            {
                return new Result(candidates[i], i > 0, false);
            }
        }

        return new Result(Truncate(candidates[^1], maxLength.Value), true, true);
    }

    /// <summary>Ordered from most detailed to most compact.</summary>
    public static IEnumerable<string> Candidates(DeviceSnapshot s)
    {
        var device = s.Identity.DisplayName;
        var deviceShort = ShortDeviceName(s);
        var cpuFull = s.Processor.Name.HasValue ? s.Processor.Name.Value! : null;
        var cpuShort = SpecSummary.ProcessorShort(s);
        var ram = SpecSummary.MemoryCompact(s);
        var storage = SpecSummary.StorageCompact(s);
        var gpu = SpecSummary.DedicatedGraphicsCompact(s);
        var noun = SpecSummary.DeviceTypeNoun(s);
        var nounTitle = char.ToUpperInvariant(noun[0]) + noun[1..];

        // Generic brand-less devices and custom builds lead with the type.
        var head = s.Identity.IsCustomBuilt || (!s.Identity.Manufacturer.HasValue && !s.Identity.ProductName.HasValue)
            ? (s.Identity.IsCustomBuilt ? "Custom-built " + (noun == "computer" ? "PC" : noun) : $"Windows {noun}")
            : device;
        if (s.Identity.IsCustomBuilt && gpu is not null)
        {
            head = "Custom gaming PC";
        }

        string Join(params string?[] specs)
        {
            var list = specs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return list.Count == 0 ? string.Empty : string.Join(", ", list);
        }

        string Compose(string h, params string?[] specs)
        {
            var tail = Join(specs);
            return tail.Length == 0 ? h : h + Separator + tail;
        }

        yield return Compose(head, cpuFull, ram is null ? null : ram + " RAM", storage, gpu);
        yield return Compose(head, cpuShort, ram is null ? null : ram + " RAM", storage, gpu);
        yield return Compose(head, cpuShort, ram is null ? null : ram + " RAM", storage);
        yield return Compose(head, cpuShort, ram, storage, gpu);
        yield return Compose(head, cpuShort, ram, storage);
        yield return Compose(deviceShort ?? head, cpuShort, ram, storage);
        yield return Compose(deviceShort ?? head, cpuShort, ram);
        yield return Compose(deviceShort ?? head, ram, storage);
        yield return Compose(deviceShort ?? head, cpuShort);
        yield return deviceShort ?? head;
        if (!string.Equals(deviceShort ?? head, nounTitle, StringComparison.Ordinal))
        {
            yield return nounTitle;
        }
    }

    private static string? ShortDeviceName(DeviceSnapshot s)
    {
        var id = s.Identity;
        if (id.IsCustomBuilt) return null;
        if (id.Manufacturer.HasValue && id.ProductName.HasValue)
        {
            // "Lenovo IdeaPad Slim 5 14IAH8" -> "Lenovo IdeaPad Slim 5"
            var product = id.ProductName.Value!;
            var words = product.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 2 && words[^1].Any(char.IsDigit) && words[^1].Any(char.IsLetter) && words[^1].Length >= 5)
            {
                product = string.Join(' ', words[..^1]);
            }
            return product.StartsWith(id.Manufacturer.Value!, StringComparison.OrdinalIgnoreCase) ? product : $"{id.Manufacturer.Value} {product}";
        }
        return null;
    }

    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            cut = cut[..lastSpace];
        }
        return cut.TrimEnd(' ', ',', '-', '–', '/', '+');
    }
}

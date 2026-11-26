namespace SellSpec.App.Converters;

/// <summary>Shared converter instances for templates that cannot reference application resources (e.g. the report document).</summary>
public static class Statics
{
    public static StringToVisibilityConverter StringToVisibility { get; } = new();
    public static CountToVisibilityConverter CountToVisibility { get; } = new();
}

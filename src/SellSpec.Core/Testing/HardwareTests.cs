namespace SellSpec.Core.Testing;

public enum HardwareTestKind
{
    Keyboard,
    Display,
    Speakers,
    Microphone,
    Webcam,
    Connectivity,
}

public enum TestOutcome
{
    NotTested,
    Passed,
    PassedWithNotes,
    Failed,
    Skipped,
}

public enum TestSource
{
    /// <summary>The seller looked at the result and confirmed it.</summary>
    SellerConfirmed,

    /// <summary>Measured by SellSpec without seller judgement (e.g. microphone level detected).</summary>
    Automatic,
}

public sealed record HardwareTestResult(
    HardwareTestKind Kind,
    TestOutcome Outcome,
    TestSource Source,
    string? Notes = null,
    string? Evidence = null,
    DateTimeOffset? CompletedAt = null)
{
    public bool IsPass => Outcome is TestOutcome.Passed or TestOutcome.PassedWithNotes;
    public bool IsFail => Outcome == TestOutcome.Failed;
    public bool WasTested => Outcome is not (TestOutcome.NotTested or TestOutcome.Skipped);

    public static HardwareTestResult NotTested(HardwareTestKind kind) => new(kind, TestOutcome.NotTested, TestSource.SellerConfirmed);
}

public static class HardwareTestCatalog
{
    public static IReadOnlyList<HardwareTestKind> All { get; } = Enum.GetValues<HardwareTestKind>();

    public static string Title(HardwareTestKind kind) => kind switch
    {
        HardwareTestKind.Keyboard => "Keyboard",
        HardwareTestKind.Display => "Display",
        HardwareTestKind.Speakers => "Speakers",
        HardwareTestKind.Microphone => "Microphone",
        HardwareTestKind.Webcam => "Webcam",
        HardwareTestKind.Connectivity => "Connectivity",
        _ => kind.ToString(),
    };

    public static string Description(HardwareTestKind kind) => kind switch
    {
        HardwareTestKind.Keyboard => "Press every key and confirm each one lights up on the on-screen keyboard.",
        HardwareTestKind.Display => "Show full-screen red, green, blue, white and black to spot dead pixels, bleed or marks.",
        HardwareTestKind.Speakers => "Play a short test tone through the left, right and both speakers.",
        HardwareTestKind.Microphone => "Watch the input level meter while speaking. Nothing is recorded or saved.",
        HardwareTestKind.Webcam => "Open a local camera preview to confirm the camera works. Nothing is recorded or saved.",
        HardwareTestKind.Connectivity => "Confirm Wi-Fi, Bluetooth, Ethernet and ports behave as expected.",
        _ => string.Empty,
    };

    public static string OutcomeLabel(TestOutcome outcome) => outcome switch
    {
        TestOutcome.NotTested => "Not tested",
        TestOutcome.Passed => "Passed",
        TestOutcome.PassedWithNotes => "Passed with notes",
        TestOutcome.Failed => "Failed",
        TestOutcome.Skipped => "Skipped",
        _ => outcome.ToString(),
    };
}

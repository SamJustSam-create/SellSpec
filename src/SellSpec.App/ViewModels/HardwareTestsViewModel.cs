using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.App.Services;
using SellSpec.Core.Models;
using SellSpec.Core.Testing;
using SellSpec.Windows.Audio;

namespace SellSpec.App.ViewModels;

public sealed partial class HardwareTestsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public HardwareTestsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        foreach (var kind in HardwareTestCatalog.All)
        {
            Tests.Add(new HardwareTestCardViewModel(shell, kind));
        }
        _shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult)) RefreshAll();
        };
        // Results also change outside the cards: a rescan of another device clears them, remembered results come back.
        _shell.Session.Tests.Changed += (_, _) => RefreshAll();
    }

    public ShellViewModel Shell => _shell;
    public ObservableCollection<HardwareTestCardViewModel> Tests { get; } = new();

    private void RefreshAll()
    {
        foreach (var t in Tests) t.RefreshFromSession();
        NotifyCounts();
    }

    public int CompletedCount => Tests.Count(t => t.Outcome != TestOutcome.NotTested);

    public void NotifyCounts() => OnPropertyChanged(nameof(CompletedCount));
}

/// <summary>One test card. Kind-specific behaviour lives in a handful of commands; the result is always seller-confirmed.</summary>
public sealed partial class HardwareTestCardViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly SoundService _sound = new();
    private MicrophoneLevelMeter? _meter;
    private float _peak;

    public HardwareTestCardViewModel(ShellViewModel shell, HardwareTestKind kind)
    {
        _shell = shell;
        Kind = kind;
        Title = HardwareTestCatalog.Title(kind);
        Description = HardwareTestCatalog.Description(kind);
        Glyph = kind switch
        {
            HardwareTestKind.Keyboard => "",
            HardwareTestKind.Display => "",
            HardwareTestKind.Speakers => "",
            HardwareTestKind.Microphone => "",
            HardwareTestKind.Webcam => "",
            HardwareTestKind.Connectivity => "",
            _ => "",
        };
        RefreshFromSession();
    }

    public HardwareTestKind Kind { get; }
    public string Title { get; }
    public string Description { get; }
    public string Glyph { get; }

    public bool IsKeyboard => Kind == HardwareTestKind.Keyboard;
    public bool IsDisplay => Kind == HardwareTestKind.Display;
    public bool IsSpeakers => Kind == HardwareTestKind.Speakers;
    public bool IsMicrophone => Kind == HardwareTestKind.Microphone;
    public bool IsWebcam => Kind == HardwareTestKind.Webcam;
    public bool IsConnectivity => Kind == HardwareTestKind.Connectivity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private TestOutcome _outcome = TestOutcome.NotTested;

    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string? _evidence;
    [ObservableProperty] private string? _hint;

    // Microphone
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private double _level;
    [ObservableProperty] private double _peakLevel;

    // Connectivity confirmations
    [ObservableProperty] private bool _wifiConfirmed;
    [ObservableProperty] private bool _bluetoothConfirmed;
    [ObservableProperty] private bool _ethernetConfirmed;
    [ObservableProperty] private bool _usbConfirmed;
    [ObservableProperty] private bool _videoOutConfirmed;

    public HealthStatusKind Status => Outcome switch
    {
        TestOutcome.Passed => HealthStatusKind.Good,
        TestOutcome.PassedWithNotes => HealthStatusKind.Attention,
        TestOutcome.Failed => HealthStatusKind.Issue,
        _ => HealthStatusKind.Unknown,
    };

    public string StatusLabel => HardwareTestCatalog.OutcomeLabel(Outcome);

    public string DetectedSummary
    {
        get
        {
            var s = _shell.Session.Snapshot;
            if (s is null) return string.Empty;
            return Kind switch
            {
                HardwareTestKind.Webcam => s.Connectivity.Cameras.Count > 0 ? "Detected: " + string.Join(", ", s.Connectivity.Cameras) : "No camera was detected on this device.",
                HardwareTestKind.Microphone => s.Connectivity.Audio is { HasValue: true, Value: true } ? "Audio device detected." : "No audio device was detected.",
                HardwareTestKind.Speakers => s.Connectivity.AudioDevices.Count > 0 ? "Detected: " + string.Join(", ", s.Connectivity.AudioDevices.Take(2)) : "No audio device was detected.",
                HardwareTestKind.Connectivity => string.Join(" · ", new[]
                {
                    s.Connectivity.WifiAdapter.HasValue ? "Wi-Fi detected" : "No Wi-Fi adapter",
                    s.Connectivity.Bluetooth is { HasValue: true, Value: true } ? "Bluetooth detected" : "No Bluetooth",
                    s.Connectivity.Ethernet is { HasValue: true, Value: true } ? "Ethernet detected" : "No Ethernet",
                }),
                HardwareTestKind.Display => s.PrimaryDisplay is { } d ? $"Testing: {d.LocationLabel}" + (d.NativeResolution.HasValue ? $" {d.NativeResolution.Value}" : string.Empty) : string.Empty,
                _ => string.Empty,
            };
        }
    }

    private bool _refreshing;

    /// <summary>Mirrors the session's result into the card without writing it back, so refreshing never alters the stored result.</summary>
    public void RefreshFromSession()
    {
        var result = _shell.Session.Tests.Get(Kind);
        _refreshing = true;
        try
        {
            Outcome = result.Outcome;
            var notes = result.Notes ?? string.Empty;
            if (Notes.Trim() != notes) Notes = notes; // keep what the seller is typing when only whitespace differs
            Evidence = result.Evidence;
            if (Kind == HardwareTestKind.Connectivity)
            {
                var evidence = result.Evidence ?? string.Empty;
                WifiConfirmed = evidence.Contains("Wi-Fi", StringComparison.Ordinal);
                BluetoothConfirmed = evidence.Contains("Bluetooth", StringComparison.Ordinal);
                EthernetConfirmed = evidence.Contains("Ethernet", StringComparison.Ordinal);
                UsbConfirmed = evidence.Contains("USB ports", StringComparison.Ordinal);
                VideoOutConfirmed = evidence.Contains("video output", StringComparison.Ordinal);
            }
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(nameof(DetectedSummary));
    }

    partial void OnOutcomeChanged(TestOutcome value) => Persist();
    partial void OnNotesChanged(string value)
    {
        if (Outcome != TestOutcome.NotTested) Persist();
    }

    partial void OnWifiConfirmedChanged(bool value) => PersistConfirmation();
    partial void OnBluetoothConfirmedChanged(bool value) => PersistConfirmation();
    partial void OnEthernetConfirmedChanged(bool value) => PersistConfirmation();
    partial void OnUsbConfirmedChanged(bool value) => PersistConfirmation();
    partial void OnVideoOutConfirmedChanged(bool value) => PersistConfirmation();

    /// <summary>Connectivity confirmations are the evidence for a recorded outcome, so they are kept with it as they change.</summary>
    private void PersistConfirmation()
    {
        if (!_refreshing && Outcome != TestOutcome.NotTested) Persist();
    }

    private void Persist()
    {
        if (_refreshing) return;
        var evidence = Evidence;
        if (Kind == HardwareTestKind.Connectivity)
        {
            var confirmed = new List<string>();
            if (WifiConfirmed) confirmed.Add("Wi-Fi");
            if (BluetoothConfirmed) confirmed.Add("Bluetooth");
            if (EthernetConfirmed) confirmed.Add("Ethernet");
            if (UsbConfirmed) confirmed.Add("USB ports");
            if (VideoOutConfirmed) confirmed.Add("video output");
            evidence = confirmed.Count > 0 ? "Seller confirmed: " + string.Join(", ", confirmed) : null;
        }
        _shell.Session.Tests.Set(new HardwareTestResult(Kind, Outcome, TestSource.SellerConfirmed, string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(), evidence));
        Evidence = evidence;
    }

    [RelayCommand]
    private void MarkPassed() => Outcome = TestOutcome.Passed;

    [RelayCommand]
    private void MarkPassedWithNotes() => Outcome = TestOutcome.PassedWithNotes;

    [RelayCommand]
    private void MarkFailed() => Outcome = TestOutcome.Failed;

    [RelayCommand]
    private void ClearResult()
    {
        Outcome = TestOutcome.NotTested;
        Notes = string.Empty;
        Evidence = null;
    }

    [RelayCommand]
    private void RunKeyboardTest()
    {
        var window = new Views.KeyboardTestWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        if (window.PressedCount > 0)
        {
            Evidence = $"{window.PressedCount} of {window.TotalKeys} keys registered";
            Hint = window.PressedCount == window.TotalKeys ? "Every key on the on-screen layout registered." : "Some keys did not register. Confirm whether the missing keys exist on this keyboard before marking the result.";
            if (Outcome != TestOutcome.NotTested) Persist();
        }
    }

    [RelayCommand]
    private void RunDisplayTest()
    {
        var window = new Views.DisplayTestWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        Hint = "Look for dead or stuck pixels, bright spots, bleed along the edges and marks on each colour.";
    }

    [RelayCommand]
    private void PlayTone(string channel)
    {
        var tone = channel switch
        {
            "left" => ToneChannel.Left,
            "right" => ToneChannel.Right,
            _ => ToneChannel.Both,
        };
        try
        {
            _sound.Play(TestToneGenerator.CreateWav(channel: tone));
            Hint = channel switch { "left" => "Playing in the left channel.", "right" => "Playing in the right channel.", _ => "Playing in both channels." };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
        {
            Hint = "The tone could not be played. Check that an audio output device is available.";
        }
    }

    [RelayCommand]
    private void ToggleMicrophone()
    {
        if (IsListening)
        {
            StopMicrophone();
            return;
        }
        _meter = new MicrophoneLevelMeter();
        _meter.LevelChanged += OnLevel;
        var error = _meter.Start();
        if (error is not null)
        {
            Hint = error;
            _meter.Dispose();
            _meter = null;
            return;
        }
        _peak = 0;
        PeakLevel = 0;
        IsListening = true;
        Hint = "Speak normally. The meter shows the input level; nothing is recorded.";
    }

    private void OnLevel(float rms, float peak)
    {
        if (peak > _peak) _peak = peak;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Level = Math.Min(1, rms * 4);
            PeakLevel = Math.Min(1, _peak * 2);
        });
    }

    private void StopMicrophone()
    {
        if (_meter is not null)
        {
            _meter.LevelChanged -= OnLevel;
            _meter.Dispose();
            _meter = null;
        }
        IsListening = false;
        Level = 0;
        if (_peak > 0)
        {
            Evidence = $"Peak input level {Math.Round(Math.Min(1, _peak * 2) * 100)}%";
            Hint = _peak < 0.02f ? "Very little input was detected. Check the microphone privacy setting or that nothing is muted." : "Input was detected. Confirm the level moved while you spoke.";
            if (Outcome != TestOutcome.NotTested) Persist();
        }
    }

    [RelayCommand]
    private void OpenCameraApp()
    {
        Hint = ExternalLauncher.OpenUri("microsoft.windows.camera:")
            ? "The Windows Camera app has opened. Nothing is recorded by SellSpec; close the app when you have checked the picture."
            : "The Windows Camera app could not be opened. Any camera app can be used for this check.";
    }

    public void Dispose()
    {
        StopMicrophone();
        _sound.Stop();
    }
}

using System.Diagnostics;

namespace SellSpec.App;

/// <summary>Command-line switches understood by SellSpec.</summary>
public sealed record StartupOptions(bool DemoMode, bool Portable, string? CaptureDirectory, bool ElevatedRescan)
{
    public static StartupOptions Parse(string[] args)
    {
        var demo = false;
        var portable = false;
        var elevated = false;
        string? capture = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--demo":
                    demo = true;
                    break;
                case "--portable":
                    portable = true;
                    break;
                case "--elevated-rescan":
                    elevated = true;
                    break;
                case "--capture" when i + 1 < args.Length:
                    capture = args[++i];
                    break;
            }
        }
        return new StartupOptions(demo, portable, capture, elevated);
    }
}

public static class Program
{
    public static Stopwatch StartupStopwatch { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        StartupStopwatch = Stopwatch.StartNew();
        var options = StartupOptions.Parse(args);
        var app = new App(options);
        app.InitializeComponent();
        return app.Run();
    }
}

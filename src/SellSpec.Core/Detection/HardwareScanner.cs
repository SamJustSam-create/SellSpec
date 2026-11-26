using System.Collections.Concurrent;
using System.Diagnostics;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Models;

namespace SellSpec.Core.Detection;

/// <summary>
/// Runs every hardware provider concurrently, isolating failures and timeouts so that one broken WMI class can
/// never take the whole scan down. The result always contains a snapshot; missing pieces are marked unavailable.
/// </summary>
public sealed class HardwareScanner
{
    private readonly HardwareProviderSet _providers;
    private readonly ScanOptions _options;
    private readonly IScanLogger _logger;

    public const int ComponentCount = 9;

    public HardwareScanner(HardwareProviderSet providers, ScanOptions? options = null, IScanLogger? logger = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? ScanOptions.Default;
        _logger = logger ?? NullScanLogger.Instance;
    }

    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.Now;
        var issues = new ConcurrentBag<ScanIssue>();
        var completed = 0;

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(_options.OverallTimeout);
        var token = overall.Token;

        _logger.Log(LogLevel.Info, "Scan", $"Scan started (elevated: {_options.IsElevated}).");

        var identityTask = RunAsync(_providers.Identity, DeviceIdentityResult.Empty, _options.ComponentTimeout);
        var processorTask = RunAsync(_providers.Processor, ProcessorInfo.Unavailable("Processor details could not be read."), _options.ComponentTimeout);
        var memoryTask = RunAsync(_providers.Memory, MemoryInfo.Unavailable("Memory details could not be read."), _options.ComponentTimeout);
        var storageTask = RunAsync(_providers.Storage, StorageResult.Empty, _options.SlowComponentTimeout);
        var graphicsTask = RunAsync(_providers.Graphics, (IReadOnlyList<GraphicsAdapter>)Array.Empty<GraphicsAdapter>(), _options.ComponentTimeout);
        var displayTask = RunAsync(_providers.Displays, (IReadOnlyList<DisplayInfo>)Array.Empty<DisplayInfo>(), _options.ComponentTimeout);
        var batteryTask = RunAsync(_providers.Battery, BatteryResult.Empty, _options.ComponentTimeout);
        var connectivityTask = RunAsync(_providers.Connectivity, ConnectivityInfo.Unavailable("Connectivity details could not be read."), _options.ComponentTimeout);
        var osTask = RunAsync(_providers.OperatingSystem, OperatingSystemInfo.Unavailable("Operating system details could not be read."), _options.SlowComponentTimeout);

        await Task.WhenAll(identityTask, processorTask, memoryTask, storageTask, graphicsTask, displayTask, batteryTask, connectivityTask, osTask).ConfigureAwait(false);

        var identity = identityTask.Result;
        var storage = storageTask.Result;
        var battery = batteryTask.Result;

        foreach (var issue in storage.Issues)
        {
            issues.Add(issue);
        }

        var privateIds = identity.Private with
        {
            StorageSerialNumbers = storage.SerialNumbers,
            BatterySerialNumbers = battery.SerialNumbers,
        };

        // Make sure nothing logged from here on can leak an identifier.
        LogSanitiser.RegisterPrivateValues(privateIds.AllValues());

        var snapshot = new DeviceSnapshot
        {
            Identity = identity.Identity,
            Processor = processorTask.Result,
            Memory = memoryTask.Result,
            Storage = storage.Devices,
            Graphics = graphicsTask.Result,
            Displays = displayTask.Result,
            Battery = battery.Battery,
            Connectivity = connectivityTask.Result,
            OperatingSystem = osTask.Result,
            Private = privateIds,
        };

        snapshot = SnapshotEnricher.Enrich(snapshot, issues);

        stopwatch.Stop();
        var wasCancelled = cancellationToken.IsCancellationRequested;
        if (overall.IsCancellationRequested && !wasCancelled)
        {
            issues.Add(new ScanIssue("Scan", "The scan hit its overall time limit; some details were left unavailable.", ScanIssueSeverity.Warning));
        }

        _logger.Log(LogLevel.Info, "Scan", $"Scan finished in {stopwatch.ElapsedMilliseconds} ms with {issues.Count} issue(s).");

        return new ScanResult
        {
            Snapshot = snapshot,
            Issues = issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Component).ToList(),
            Duration = stopwatch.Elapsed,
            ScannedAt = startedAt,
            WasCancelled = wasCancelled,
            IsElevated = _options.IsElevated,
        };

        async Task<T> RunAsync<T>(IHardwareProvider<T> provider, T fallback, TimeSpan timeout)
        {
            var name = provider.ComponentName;
            progress?.Report(new ScanProgress(name, ScanStage.Started, Volatile.Read(ref completed), ComponentCount));
            var componentWatch = Stopwatch.StartNew();
            var stage = ScanStage.Completed;
            T result = fallback;

            using var componentCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            componentCts.CancelAfter(timeout);

            try
            {
                result = await provider.DetectAsync(componentCts.Token).WaitAsync(timeout, token).ConfigureAwait(false);
                _logger.Log(LogLevel.Debug, name, $"Completed in {componentWatch.ElapsedMilliseconds} ms.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stage = ScanStage.Cancelled;
                issues.Add(new ScanIssue(name, "Scan cancelled before this component finished.", ScanIssueSeverity.Info));
            }
            catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException)
            {
                stage = ScanStage.TimedOut;
                issues.Add(new ScanIssue(name, $"{name} detection took too long and was skipped.", ScanIssueSeverity.Warning, "Timed out."));
                _logger.Log(LogLevel.Warning, name, $"Timed out after {componentWatch.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                stage = ScanStage.Failed;
                issues.Add(new ScanIssue(name, $"{name} details could not be read.", ScanIssueSeverity.Error, ex.GetType().Name));
                _logger.Log(LogLevel.Error, name, "Detection failed.", ex);
            }
            finally
            {
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress(name, stage, done, ComponentCount));
            }

            return result;
        }
    }
}

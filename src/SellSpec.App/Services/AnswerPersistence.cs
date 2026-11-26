using System.IO;
using System.Windows.Threading;
using SellSpec.App.ViewModels;
using SellSpec.Core.Diagnostics;
using SellSpec.Core.Privacy;
using SellSpec.Core.Sessions;

namespace SellSpec.App.Services;

/// <summary>
/// Remembers the questionnaire answers and hardware test results for the current device model in the data folder a
/// moment after they change, so they survive a restart or an administrator rescan. Nothing is written in demo mode,
/// when the setting is off or when the device cannot be identified, and nothing that fails the redaction check.
/// </summary>
public sealed class AnswerPersistence : IDisposable
{
    private const string NotRememberedPrefix = "Your answers were not remembered: ";

    private readonly AppSession _session;
    private readonly ISavedAnswersStore _store;
    private readonly IScanLogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _dirty;

    public AnswerPersistence(AppSession session, ISavedAnswersStore store, IScanLogger logger)
    {
        _session = session;
        _store = store;
        _logger = logger;
        // Text boxes update on every keystroke; wait for a pause before touching the disk.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, _) => Flush();
        _session.Questionnaire.Changed += OnChanged;
        _session.Tests.Changed += OnChanged;
    }

    /// <summary>True while the current scan is a real, identified device and the seller has not turned remembering off.</summary>
    public bool CanRemember =>
        _session.Snapshot is not null &&
        SavedAnswersPolicy.CanRemember(_session.Settings.RememberAnswers, _session.IsDemo, _session.CurrentDeviceKey);

    /// <summary>Writes the current answers straight away (after the setting is turned on).</summary>
    public void SaveNow()
    {
        _dirty = true;
        Flush();
    }

    /// <summary>Writes any pending change now; called before an elevated relaunch and on exit.</summary>
    public void Flush()
    {
        _timer.Stop();
        if (!_dirty) return;
        _dirty = false;
        if (!CanRemember) return;

        var snapshot = _session.Snapshot!;
        var entry = new SavedDeviceAnswers
        {
            DeviceKey = _session.CurrentDeviceKey!,
            DeviceLabel = snapshot.Identity.DisplayName,
            SavedAt = DateTimeOffset.Now,
            AppVersion = AppInfo.Version,
            Questionnaire = _session.Condition,
            Tests = _session.Tests.Results,
        };

        // An untouched session must not erase the answers that are being offered back.
        if (entry.IsEmpty && _session.SavedAnswersOffer is not null) return;

        try
        {
            _store.Save(entry, snapshot.Private);
            if (_session.SavedAnswersStatus?.StartsWith(NotRememberedPrefix, StringComparison.Ordinal) == true)
            {
                _session.SavedAnswersStatus = null;
            }
        }
        catch (RedactionException ex)
        {
            var findings = string.Join(", ", ex.Findings);
            _session.SavedAnswersStatus = NotRememberedPrefix + "they contain " + findings + ". Remove it and they will be remembered again.";
            _logger.Log(LogLevel.Warning, "Answers", "Answers were not remembered because they contain " + findings + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Answers", "Answers could not be written to the data folder.", ex);
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        // Once the seller starts answering, the remembered set no longer applies: it is about to be replaced.
        if (_session.SavedAnswersOffer is not null && _session.HasAnswers)
        {
            _session.SavedAnswersOffer = null;
        }
        _dirty = true;
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _session.Questionnaire.Changed -= OnChanged;
        _session.Tests.Changed -= OnChanged;
    }
}

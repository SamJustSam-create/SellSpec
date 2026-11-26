using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellSpec.Core.Health;
using SellSpec.Core.Models;

namespace SellSpec.App.ViewModels;

public sealed record HealthGroup(string Title, IReadOnlyList<HealthItem> Items);

public sealed partial class DeviceHealthViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public DeviceHealthViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.ScanResult)) Refresh();
        };
        _shell.Session.Tests.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<HealthGroup> Groups { get; } = new();

    [ObservableProperty] private bool _offerElevatedRescan;
    [ObservableProperty] private int _goodCount;
    [ObservableProperty] private int _attentionCount;
    [ObservableProperty] private int _issueCount;
    [ObservableProperty] private int _unknownCount;

    [RelayCommand]
    private void RescanElevated() => _shell.RescanElevatedCommand.Execute(null);

    [RelayCommand]
    private void OpenTests() => _shell.Navigate("tests");

    private void Refresh()
    {
        Groups.Clear();
        var s = _shell.Session.Snapshot;
        if (s is null) return;

        var items = HealthDashboardBuilder.Build(s, _shell.Session.Tests.Results);
        foreach (var group in items.GroupBy(i => i.Group))
        {
            Groups.Add(new HealthGroup(group.Key, group.ToList()));
        }

        GoodCount = items.Count(i => i.Status == HealthStatusKind.Good);
        AttentionCount = items.Count(i => i.Status == HealthStatusKind.Attention);
        IssueCount = items.Count(i => i.Status == HealthStatusKind.Issue);
        UnknownCount = items.Count(i => i.Status is HealthStatusKind.Unknown);

        OfferElevatedRescan = !_shell.Session.IsElevated && !_shell.Session.IsDemo &&
                              s.Storage.Any(d => d.Location == StorageLocation.Internal && !d.PowerOnHours.HasValue);
    }
}

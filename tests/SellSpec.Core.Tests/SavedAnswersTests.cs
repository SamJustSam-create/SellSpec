using SellSpec.Core.Listing;
using SellSpec.Core.Models;
using SellSpec.Core.Privacy;
using SellSpec.Core.Sessions;
using SellSpec.Core.Testing;
using Xunit;

namespace SellSpec.Core.Tests;

public sealed class DeviceKeyTests
{
    [Fact]
    public void Branded_device_is_keyed_by_brand_product_and_model()
    {
        Assert.Equal("lenovo|ideapad slim 5 14iah8|82xf", DeviceKey.For(Scenarios.StandardLaptop()));
    }

    [Fact]
    public void Custom_build_is_keyed_by_its_motherboard()
    {
        Assert.Equal("custom|gigabyte|b550m k", DeviceKey.For(Scenarios.CustomDesktop()));
    }

    [Fact]
    public void Unidentifiable_devices_have_no_key()
    {
        Assert.Null(DeviceKey.For(Scenarios.VirtualMachine()));
        Assert.Null(DeviceKey.For(Scenarios.IncompleteManufacturer()));
        Assert.Null(DeviceKey.For(new DeviceIdentity { Manufacturer = Spec<string>.Firmware("HP") }));
    }

    [Fact]
    public void Key_ignores_case_and_spacing_but_keeps_missing_parts_distinct()
    {
        var shouty = Scenarios.StandardLaptop().Identity with
        {
            Manufacturer = Spec<string>.Firmware(" LENOVO"),
            ProductName = Spec<string>.Firmware("IdeaPad  Slim 5\t14IAH8 "),
        };
        Assert.Equal(DeviceKey.For(Scenarios.StandardLaptop()), DeviceKey.For(shouty));

        var noModel = Scenarios.StandardLaptop().Identity with { ModelNumber = Spec<string>.Unavailable() };
        Assert.Equal("lenovo|ideapad slim 5 14iah8|-", DeviceKey.For(noModel));
    }

    [Fact]
    public void Key_never_contains_a_private_identifier()
    {
        var key = DeviceKey.For(Scenarios.StandardLaptop())!;
        foreach (var value in Scenarios.PrivateIds.AllValues())
        {
            Assert.DoesNotContain(value, key, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed class SavedAnswersStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SellSpec.Tests", Guid.NewGuid().ToString("N"));
    private readonly JsonSavedAnswersStore _store;

    public SavedAnswersStoreTests()
    {
        _store = new JsonSavedAnswersStore(Path.Combine(_directory, "answers.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static SavedDeviceAnswers Entry(string key = "lenovo|ideapad slim 5 14iah8|82xf", string? notes = "Light scuff on the lid") => new()
    {
        DeviceKey = key,
        DeviceLabel = "Lenovo IdeaPad Slim 5 14IAH8",
        SavedAt = new DateTimeOffset(2026, 11, 26, 14, 30, 0, TimeSpan.FromHours(11)),
        AppVersion = "0.1.2-alpha",
        Questionnaire = new ConditionQuestionnaire
        {
            OverallCondition = ConditionGrade.Good,
            HasCosmeticDamage = Answer.Yes,
            CosmeticDamageNotes = notes,
            ChargerIncluded = Answer.Yes,
            AskingPrice = 650m,
        },
        Tests = new[]
        {
            new HardwareTestResult(HardwareTestKind.Keyboard, TestOutcome.Passed, TestSource.SellerConfirmed, null, "78 of 78 keys registered", new DateTimeOffset(2026, 11, 26, 14, 0, 0, TimeSpan.FromHours(11))),
            new HardwareTestResult(HardwareTestKind.Speakers, TestOutcome.PassedWithNotes, TestSource.SellerConfirmed, "Left is a little quieter", null, new DateTimeOffset(2026, 11, 26, 14, 5, 0, TimeSpan.FromHours(11))),
        },
    };

    [Fact]
    public void Round_trips_answers_and_test_results()
    {
        var entry = Entry();
        _store.Save(entry, Scenarios.PrivateIds);

        var found = _store.Find(entry.DeviceKey);

        Assert.NotNull(found);
        Assert.Equal(entry.DeviceLabel, found!.DeviceLabel);
        Assert.Equal(entry.SavedAt, found.SavedAt);
        Assert.Equal(entry.AppVersion, found.AppVersion);
        Assert.Equal(entry.Questionnaire, found.Questionnaire);
        Assert.Equal(entry.Tests, found.Tests);
        Assert.Null(_store.Find("dell|xps 15 9520|0a1b"));
    }

    [Fact]
    public void Saving_again_replaces_the_device_and_an_empty_entry_removes_it()
    {
        _store.Save(Entry(), Scenarios.PrivateIds);
        _store.Save(Entry(notes: "Replaced note"), Scenarios.PrivateIds);
        Assert.Single(_store.All());
        Assert.Equal("Replaced note", _store.Find(Entry().DeviceKey)!.Questionnaire.CosmeticDamageNotes);

        _store.Save(new SavedDeviceAnswers { DeviceKey = Entry().DeviceKey, DeviceLabel = "x" }, Scenarios.PrivateIds);
        Assert.Null(_store.Find(Entry().DeviceKey));
        Assert.False(File.Exists(_store.Path), "The file is deleted once no device remains.");
    }

    [Fact]
    public void Keeps_the_newest_devices_first_and_caps_the_history()
    {
        for (var i = 0; i < JsonSavedAnswersStore.MaxDevices + 2; i++)
        {
            _store.Save(Entry(key: $"brand|model {i}|-") with { SavedAt = default }, Scenarios.PrivateIds);
        }

        var all = _store.All();
        Assert.Equal(JsonSavedAnswersStore.MaxDevices, all.Count);
        Assert.Equal($"brand|model {JsonSavedAnswersStore.MaxDevices + 1}|-", all[0].DeviceKey);
        Assert.Null(_store.Find("brand|model 0|-"));
        Assert.All(all, d => Assert.NotEqual(default, d.SavedAt));
    }

    [Fact]
    public void Remove_and_clear()
    {
        _store.Save(Entry(key: "a|b|c"), Scenarios.PrivateIds);
        _store.Save(Entry(key: "d|e|f"), Scenarios.PrivateIds);

        _store.Remove("a|b|c");
        Assert.Null(_store.Find("a|b|c"));
        Assert.NotNull(_store.Find("d|e|f"));

        _store.Remove("never saved");
        Assert.Single(_store.All());

        _store.Clear();
        Assert.Empty(_store.All());
        Assert.False(File.Exists(_store.Path));
    }

    [Fact]
    public void Corrupt_or_missing_file_reads_as_empty_and_is_overwritten_on_save()
    {
        Assert.Empty(_store.All());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.Path, "{ not json");
        Assert.Empty(_store.All());
        Assert.Null(_store.Find(Entry().DeviceKey));

        _store.Save(Entry(), Scenarios.PrivateIds);
        Assert.NotNull(_store.Find(Entry().DeviceKey));
    }

    [Fact]
    public void Refuses_to_write_a_private_identifier_and_leaves_the_file_untouched()
    {
        _store.Save(Entry(), Scenarios.PrivateIds);
        var before = File.ReadAllText(_store.Path);

        var leaking = Entry(notes: "Serial " + Scenarios.PrivateIds.SystemSerialNumber + " on the base");
        var ex = Assert.Throws<RedactionException>(() => _store.Save(leaking, Scenarios.PrivateIds));

        Assert.Contains("private identifier", ex.Findings);
        Assert.Equal(before, File.ReadAllText(_store.Path));
        Assert.False(File.Exists(_store.Path + ".tmp"));
    }

    [Fact]
    public void Saved_file_contains_no_identifiers_and_no_derived_flags()
    {
        _store.Save(Entry(), Scenarios.PrivateIds);
        var json = File.ReadAllText(_store.Path);

        Assert.Empty(Redactor.FindLeaks(json, Scenarios.PrivateIds));
        Assert.DoesNotContain("isPass", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isEmpty", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Good\"", json); // enums are written as names so the file stays readable
    }

    [Fact]
    public void Missing_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _store.Save(Entry(key: " "), Scenarios.PrivateIds));
    }
}

public sealed class SavedAnswersPolicyTests
{
    private static readonly SavedDeviceAnswers Saved = new()
    {
        DeviceKey = "k",
        DeviceLabel = "Device",
        Questionnaire = new ConditionQuestionnaire { OverallCondition = ConditionGrade.Fair },
    };

    [Fact]
    public void Nothing_saved_or_answers_already_entered_means_no_action()
    {
        Assert.Equal(SavedAnswersAction.None, SavedAnswersPolicy.Decide(null, sessionHasAnswers: false, isElevatedRescan: false));
        Assert.Equal(SavedAnswersAction.None, SavedAnswersPolicy.Decide(new SavedDeviceAnswers { DeviceKey = "k" }, false, false));
        Assert.Equal(SavedAnswersAction.None, SavedAnswersPolicy.Decide(Saved, sessionHasAnswers: true, isElevatedRescan: false));
        Assert.Equal(SavedAnswersAction.None, SavedAnswersPolicy.Decide(Saved, sessionHasAnswers: true, isElevatedRescan: true));
    }

    [Fact]
    public void Saved_answers_are_offered_normally_and_restored_after_an_elevated_rescan()
    {
        Assert.Equal(SavedAnswersAction.Offer, SavedAnswersPolicy.Decide(Saved, sessionHasAnswers: false, isElevatedRescan: false));
        Assert.Equal(SavedAnswersAction.Restore, SavedAnswersPolicy.Decide(Saved, sessionHasAnswers: false, isElevatedRescan: true));
    }

    [Fact]
    public void Rescan_keeps_the_session_only_for_the_same_device()
    {
        Assert.True(SavedAnswersPolicy.KeepSessionAcrossRescan("a", "a"));
        Assert.False(SavedAnswersPolicy.KeepSessionAcrossRescan("a", "b"));
        Assert.False(SavedAnswersPolicy.KeepSessionAcrossRescan(null, "a"));
        Assert.False(SavedAnswersPolicy.KeepSessionAcrossRescan("a", null));
    }

    [Fact]
    public void Remembering_needs_the_setting_a_real_device_and_a_key()
    {
        Assert.True(SavedAnswersPolicy.CanRemember(rememberAnswers: true, isDemo: false, deviceKey: "k"));
        Assert.False(SavedAnswersPolicy.CanRemember(false, false, "k"));
        Assert.False(SavedAnswersPolicy.CanRemember(true, true, "k"));
        Assert.False(SavedAnswersPolicy.CanRemember(true, false, null));
    }
}

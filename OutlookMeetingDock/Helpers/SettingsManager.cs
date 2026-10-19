using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OutlookMeetingDock;

internal sealed class SettingsManager
{
    private readonly Settings _settings;

    /// <summary>Raised whenever the user edits a setting in the Command Palette settings UI.</summary>
    public event EventHandler? SettingsUpdated;

    public SettingsManager()
    {
        _settings = new Settings();

        var warnMinutes = new ChoiceSetSetting(
            "warnMinutes",
            "Warning threshold",
            "How many minutes before a meeting starts the dock warns (⚠ + toast)",
            [
                new ChoiceSetSetting.Choice("15 minutes", "15"),
                new ChoiceSetSetting.Choice("5 minutes", "5"),
                new ChoiceSetSetting.Choice("10 minutes", "10"),
                new ChoiceSetSetting.Choice("30 minutes", "30"),
            ]);

        var pollMinutes = new ChoiceSetSetting(
            "pollMinutes",
            "Calendar refresh rate",
            "How often the Outlook calendar is re-read (minutes)",
            [
                new ChoiceSetSetting.Choice("5 minutes", "5"),
                new ChoiceSetSetting.Choice("1 minute", "1"),
                new ChoiceSetSetting.Choice("15 minutes", "15"),
                new ChoiceSetSetting.Choice("60 minutes", "60"),
            ]);

        var includeAllDay = new ToggleSetting(
            "includeAllDay",
            "Show all-day events",
            "When enabled, all-day events (e.g. vacations) appear in the agenda. They never trigger the countdown.",
            false);

        _settings.Add(warnMinutes);
        _settings.Add(pollMinutes);
        _settings.Add(includeAllDay);

        _settings.SettingsChanged += OnSettingsChanged;
    }

    public ICommandSettings Settings => _settings;

    public ICommand SettingsPage => _settings.SettingsPage;

    public int WarnMinutes
    {
        get
        {
            var raw = _settings.GetSetting<string>("warnMinutes");
            return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : 15;
        }
    }

    public int PollMinutes
    {
        get
        {
            var raw = _settings.GetSetting<string>("pollMinutes");
            return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : 5;
        }
    }

    public bool IncludeAllDay => _settings.GetSetting<bool>("includeAllDay");

    private void OnSettingsChanged(object sender, Settings args)
    {
        SettingsUpdated?.Invoke(this, EventArgs.Empty);
    }
}

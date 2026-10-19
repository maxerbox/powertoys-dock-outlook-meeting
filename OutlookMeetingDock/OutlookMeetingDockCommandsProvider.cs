using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OutlookMeetingDock;

public partial class OutlookMeetingDockCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    private readonly NextMeetingService _meetingService;
    private readonly MeetingsPage _meetingsPage;
    private readonly MeetingsDockPage _meetingsDockPage;

    public OutlookMeetingDockCommandsProvider()
    {
        DisplayName = "Outlook Next Meeting";
        Id = "com.outlookmeeting";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _meetingService = new NextMeetingService(_settingsManager);
        _meetingsPage = new MeetingsPage(_meetingService, _settingsManager);
        _meetingsDockPage = new MeetingsDockPage(_meetingService, _settingsManager, _meetingsPage);

        Settings = _settingsManager.Settings;
    }

    // No top-level command: the dock band is the single entry point, and
    // clicking it opens today's agenda (with settings and refresh in its
    // context menu).
    public override ICommandItem[] TopLevelCommands()
    {
        return [];
    }

    /// <summary>
    /// Dock band showing the current and next meeting with a live countdown,
    /// turning orange with a ⚠ (and firing a toast) inside the warning window.
    /// </summary>
    public override ICommandItem[]? GetDockBands()
    {
        return [new CommandItem(_meetingsDockPage) { Title = "Meetings" }];
    }
}

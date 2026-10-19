using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OutlookMeetingDock;

public partial class OutlookMeetingDockCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settingsManager = new();
    private readonly NextMeetingService _meetingService;
    private readonly MeetingsPage _meetingsPage;
    private readonly MeetingsDockPage _meetingsDockPage;
    private readonly ICommandItem[] _commands;

    public OutlookMeetingDockCommandsProvider()
    {
        DisplayName = "Outlook Next Meeting";
        Id = "com.outlookmeeting";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _meetingService = new NextMeetingService(_settingsManager);
        _meetingsPage = new MeetingsPage(_meetingService, _settingsManager);
        _meetingsDockPage = new MeetingsDockPage(_meetingService, _settingsManager, _meetingsPage);

        _commands =
        [
            new CommandItem(_meetingsPage)
            {
                Title = "Next Meeting",
                Subtitle = "Outlook calendar countdown, with room and 15-min warning",
                MoreCommands =
                [
                    new CommandContextItem(new RefreshCommand(_meetingService))
                    {
                        Title = "Refresh now",
                        Icon = new IconInfo("\uE72C"), // Refresh
                    },
                    new CommandContextItem(_settingsManager.SettingsPage)
                    {
                        Title = "Settings",
                        Icon = new IconInfo("\uE713"), // Settings
                    },
                ],
            },
        ];

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
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

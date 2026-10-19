using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OutlookMeetingDock;

/// <summary>Full palette page: today's agenda with per-meeting countdown.</summary>
internal sealed partial class MeetingsPage : ListPage
{
    private readonly NextMeetingService _service;
    private readonly SettingsManager _settings;
    private readonly OutlookCalendarClient _client = new();

    public MeetingsPage(NextMeetingService service, SettingsManager settings)
    {
        _service = service;
        _settings = settings;

        Title = "Next Meeting";
        Name = "Open";
        Id = "com.outlookmeeting.meetingspage";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        // Raised from threadpool threads — marshaled to the single page
        // thread so GetItems (host RPC thread) never races it.
        _service.StateChanged += (_, _) => UiThread.Post(() => RaiseItemsChanged());
    }

    public override IListItem[] GetItems()
    {
        var now = DateTime.Now;
        var state = _service.State;
        var warnMinutes = _settings.WarnMinutes;

        if (state.Error is not null)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Outlook is not available",
                    Subtitle = state.Error,
                    Icon = IconHelpers.FromRelativePath("Assets\\MeetingGray.png"),
                },
                new ListItem(_settings.SettingsPage)
                {
                    Title = "Open settings",
                    Subtitle = "Refresh rate and warning threshold",
                    Icon = new IconInfo("\uE713"), // Settings
                },
            ];
        }

        if (state.Meetings.Count == 0)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "No meetings today",
                    Subtitle = "Enjoy the quiet day 🎉",
                    Icon = IconHelpers.FromRelativePath("Assets\\MeetingGray.png"),
                },
                new ListItem(_settings.SettingsPage)
                {
                    Title = "Open settings",
                    Subtitle = "Refresh rate and warning threshold",
                    Icon = new IconInfo("\uE713"), // Settings
                },
            ];
        }

        return state.Meetings.Select(m => CreateListItem(m, now, warnMinutes)).ToArray();
    }

    private ListItem CreateListItem(MeetingInfo meeting, DateTime now, int warnMinutes)
    {
        // Status color of the item; the icon set (calendar vs video camera)
        // is chosen below depending on whether it is a Zoom meeting.
        string color;
        string status;
        if (meeting.AllDay)
        {
            color = "Gray";
            status = "all day";
        }
        else if (meeting.IsInProgress(now))
        {
            color = "Green";
            status = $"in progress · {MeetingFormat.Countdown(meeting.End - now)} left";
        }
        else if (meeting.Start > now)
        {
            var minutesToStart = (meeting.Start - now).TotalMinutes;
            var warning = minutesToStart <= warnMinutes;
            color = warning ? "Orange" : "Gray";
            status = warning
                ? $"⚠ starts in {MeetingFormat.Countdown(meeting.Start - now)}"
                : $"starts in {MeetingFormat.Countdown(meeting.Start - now)}";
        }
        else
        {
            color = "Gray";
            status = "finished";
        }

        // Zoom meetings get the video-camera icon in the same status color,
        // so they stand out from room-only meetings at a glance.
        var iconSet = meeting.ZoomUrl is not null ? "Zoom" : "Meeting";
        var icon = IconHelpers.FromRelativePath($"Assets\\{iconSet}{color}.png");

        var room = meeting.Room.Length > 0 ? $" · Room: {meeting.Room}" : string.Empty;
        var zoom = meeting.ZoomUrl is not null ? " · Zoom" : string.Empty;

        // On Zoom meetings, the primary action (click) joins the conference;
        // "Open in Outlook" moves to the context menu instead.
        var more = new List<CommandContextItem>();
        if (meeting.ZoomUrl is not null)
        {
            more.Add(new CommandContextItem(new OpenMeetingCommand(_client, meeting))
            {
                Title = "Open in Outlook",
                Icon = new IconInfo("\uE8A7"), // Map
            });
        }

        more.Add(new CommandContextItem(new RefreshCommand(_service))
        {
            Title = "Refresh calendar",
            Icon = new IconInfo("\uE72C"), // Refresh
        });
        more.Add(new CommandContextItem(_settings.SettingsPage)
        {
            Title = "Settings",
            Icon = new IconInfo("\uE713"), // Settings
        });

        return new ListItem(meeting.ZoomUrl is not null ? new JoinZoomCommand(meeting) : new OpenMeetingCommand(_client, meeting))
        {
            Title = meeting.Subject,
            Subtitle = $"{meeting.TimeRange}{room} · {status}{zoom}",
            Icon = icon,
            MoreCommands = [.. more],
        };
    }
}

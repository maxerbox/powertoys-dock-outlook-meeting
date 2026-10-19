using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace OutlookMeetingDock;

/// <summary>
/// The single dock widget: ONE button whose title is the live countdown to
/// the next meeting ("Point hebdo · 8m", orange + ⚠ inside the warning
/// window, "23m left" while a meeting is in progress). Clicking it opens
/// today's agenda in the palette; hovering shows the room and time range.
/// </summary>
internal sealed partial class MeetingsDockPage : ListPage
{
    private readonly NextMeetingService _service;
    private readonly SettingsManager _settings;
    private readonly MeetingsPage _agendaPage;
    private readonly OutlookCalendarClient _client = new();

    // Cached icons: reusing the same IconInfo instances avoids dock flicker.
    private static readonly IconInfo GrayIcon = IconHelpers.FromRelativePath("Assets\\MeetingGray.png");
    private static readonly IconInfo GreenIcon = IconHelpers.FromRelativePath("Assets\\MeetingGreen.png");
    private static readonly IconInfo OrangeIcon = IconHelpers.FromRelativePath("Assets\\MeetingOrange.png");

    // Video-camera variants used whenever the current/next meeting is on
    // Zoom (status-colored), so Zoom meetings are distinguishable at any
    // distance from start — not only inside the join window.
    private static readonly IconInfo ZoomGrayIcon = IconHelpers.FromRelativePath("Assets\\ZoomGray.png");
    private static readonly IconInfo ZoomGreenIcon = IconHelpers.FromRelativePath("Assets\\ZoomGreen.png");
    private static readonly IconInfo ZoomOrangeIcon = IconHelpers.FromRelativePath("Assets\\ZoomOrange.png");

    private const string SlotId = "meeting";

    /// <summary>Clicking the widget joins Zoom this many minutes before the meeting starts (or any time while it is in progress).</summary>
    private const int JoinWindowMinutes = 5;

    private ListItem? _item;

    /// <summary>Serializes item builds between the page thread and host RPC threads.</summary>
    private readonly object _gate = new();

    /// <summary>Signature of the last rendered item; used to raise ItemsChanged only when the visible content actually changed.</summary>
    private string? _lastSignature;

    public MeetingsDockPage(NextMeetingService service, SettingsManager settings, MeetingsPage agendaPage)
    {
        _service = service;
        _settings = settings;
        _agendaPage = agendaPage;

        Title = "Meetings";
        Name = "Open";
        Id = "com.outlookmeeting.dockpage";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        // StateChanged fires on threadpool threads (poll continuation + the
        // 30 s countdown tick); GetItems arrives on the host's COM RPC
        // threads. All mutations are serialized by _gate and the service only
        // POSTS updates — GetItems must NEVER block on the page thread:
        // RaiseItemsChanged is a synchronous COM call into the host, and the
        // host re-enters GetItems from that event, so a GetItems that waits
        // for the thread raising the event deadlocks (band stuck on
        // "loading...").
        _service.StateChanged += (_, _) => UiThread.Post(UpdateItem);
        _service.WarningRaised += OnWarning;
    }

    public override IListItem[] GetItems()
    {
        lock (_gate)
        {
            _item ??= NewItem(null);
            return [_item];
        }
    }

    private void OnWarning(object? sender, MeetingInfo meeting)
    {
        var room = meeting.Room.Length > 0 ? $" — Room: {meeting.Room}" : string.Empty;
        var joinHint = meeting.ZoomUrl is not null ? " — click the dock to join" : string.Empty;
        new ToastStatusMessage($"⚠ {meeting.Subject} starts in {_settings.WarnMinutes} min{room}{joinHint}").Show();
    }

    // IMPORTANT: the dock host caches view models per IListItem reference and
    // never re-reads a mutated item — it only re-fetches GetItems() after an
    // ItemsChanged. So every refresh must build a FRESH ListItem (mutating
    // the old one in place leaves the band frozen at a stale title), and
    // ItemsChanged must be raised whenever the visible content changed.
    // BuildItem runs under _gate; the raise happens on the page thread AFTER
    // the lock is released (raising under the lock can deadlock with a host
    // GetItems re-entry).
    private void UpdateItem()
    {
        string signature;
        lock (_gate)
        {
            signature = BuildItem();
        }

        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            RaiseItemsChanged();
        }
    }

    private string BuildItem()
    {
        var now = DateTime.Now;
        var state = _service.State;
        var warnMinutes = _settings.WarnMinutes;

        ListItem item;
        string signature;

        if (state.Error is not null)
        {
            item = NewItem(null);
            item.Title = "Outlook ⚠";
            item.Subtitle = state.Error;
            item.Icon = GrayIcon;
            signature = $"error|{state.Error}";
        }
        else
        {
            var current = state.Current(now);
            var next = state.Next(now);

            // The relevant meeting for the click / context actions: the one
            // in progress, else the next one.
            var meeting = current ?? next;
            var joinReady = meeting is not null
                && meeting.ZoomUrl is not null
                && now >= meeting.Start.AddMinutes(-JoinWindowMinutes)
                && now < meeting.End;

            item = NewItem(meeting, joinReady);

            if (current is not null)
            {
                // Meeting in progress: green, time left. Time FIRST — the
                // dock truncates the title from the right, so the countdown
                // must lead. "▶" signals that a click now joins the Zoom
                // conference.
                item.Title = $"{(joinReady ? "▶ " : string.Empty)}{MeetingFormat.Countdown(current.End - now)} left · {MeetingFormat.Abbreviate(current.Subject)}";
                item.Subtitle = current.Room.Length > 0
                    ? $"{current.TimeRange} · Room: {current.Room} · in progress"
                    : $"{current.TimeRange} · in progress";
                item.Icon = current.ZoomUrl is not null ? ZoomGreenIcon : GreenIcon;
                signature = $"current|{item.Title}|{current.ZoomUrl is not null}";
            }
            else if (next is not null)
            {
                var minutesToStart = (next.Start - now).TotalMinutes;
                var warning = minutesToStart > 0 && minutesToStart <= warnMinutes;

                // Time FIRST (countdown + start clock): the dock truncates
                // the title from the right, so a long subject must never
                // lead. "▶" signals that a click now joins the Zoom
                // conference.
                item.Title = $"{(joinReady ? "▶ " : string.Empty)}{(warning ? "⚠ " : string.Empty)}{MeetingFormat.Countdown(next.Start - now)} · {next.Start:HH:mm} {MeetingFormat.Abbreviate(next.Subject)}";
                item.Subtitle = next.Room.Length > 0
                    ? $"{next.TimeRange} · Room: {next.Room}"
                    : next.TimeRange;
                item.Icon = next.ZoomUrl is not null
                    ? (warning ? ZoomOrangeIcon : ZoomGrayIcon)
                    : warning ? OrangeIcon : GrayIcon;
                signature = $"next|{item.Title}|{item.Icon.GetHashCode()}";
            }
            else
            {
                item.Title = "No more meetings";
                item.Subtitle = "Nothing left on today's calendar";
                item.Icon = GrayIcon;
                signature = "none";
            }
        }

        _item = item;
        return signature;
    }

    private ListItem NewItem(MeetingInfo? meeting, bool joinReady = false)
    {
        var more = new List<CommandContextItem>();
        if (meeting?.ZoomUrl is not null)
        {
            more.Add(new CommandContextItem(new JoinZoomCommand(meeting))
            {
                Title = "Join Zoom",
                Icon = new IconInfo("\uE714"), // Video
            });
        }

        more.Add(new CommandContextItem(meeting is null ? _agendaPage : new OpenMeetingCommand(_client, meeting))
        {
            Title = "Open next meeting in Outlook",
            Icon = new IconInfo("\uE8A7"), // Map
        });
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

        // Normally the item's command IS the agenda page: the dock opens a
        // flyout with the page's list of meetings right at the widget. But
        // inside the Zoom join window, a click joins the conference directly.
        _item = new ListItem(joinReady && meeting is not null ? new JoinZoomCommand(meeting) : _agendaPage)
        {
            Title = "No more meetings",
            Subtitle = "Nothing left on today's calendar",
            Icon = GrayIcon,
            MoreCommands = [.. more],
        };

        return _item;
    }
}

/// <summary>Opens the meeting's inspector window in Outlook.</summary>
internal sealed partial class OpenMeetingCommand : InvokableCommand
{
    private readonly OutlookCalendarClient _client;
    private readonly MeetingInfo _meeting;

    public OpenMeetingCommand(OutlookCalendarClient client, MeetingInfo meeting)
    {
        _client = client;
        _meeting = meeting;
        Name = "Open in Outlook";
        Icon = new IconInfo("\uE8A7"); // Map
    }

    public override CommandResult Invoke()
    {
        try
        {
            // Never let a blocked Outlook hang the extension (same hardening
            // as the calendar poll): give the COM call 10 seconds.
            Task.Run(() => _client.DisplayMeeting(_meeting)).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            new ToastStatusMessage($"Could not open meeting: {ex.Message}").Show();
        }

        return CommandResult.Dismiss();
    }
}

/// <summary>Joins the meeting's Zoom conference directly in the installed Zoom client.</summary>
internal sealed partial class JoinZoomCommand : InvokableCommand
{
    private readonly MeetingInfo _meeting;

    public JoinZoomCommand(MeetingInfo meeting)
    {
        _meeting = meeting;
        Name = "Join Zoom";
        Icon = new IconInfo("\uE714"); // Video
    }

    public override CommandResult Invoke()
    {
        try
        {
            // zoommtg:// launches the registered protocol handler (the Zoom
            // client) straight into the meeting — no browser hop.
            var protocol = _meeting.ZoomProtocolLink
                ?? throw new InvalidOperationException("This meeting has no Zoom link");
            Process.Start(new ProcessStartInfo(protocol) { UseShellExecute = true });
            return CommandResult.Dismiss();
        }
        catch (Exception ex)
        {
            // Protocol not registered / client missing — fall back to the
            // https link, which routes through the browser into the client.
            try
            {
                if (_meeting.ZoomUrl is not null)
                {
                    Process.Start(new ProcessStartInfo(_meeting.ZoomUrl) { UseShellExecute = true });
                    return CommandResult.Dismiss();
                }
            }
            catch (Exception ex2)
            {
                ex = ex2;
            }

            new ToastStatusMessage($"Could not join Zoom: {ex.Message}").Show();
        }

        return CommandResult.KeepOpen();
    }
}

internal sealed partial class RefreshCommand : InvokableCommand
{
    private readonly NextMeetingService _service;

    public RefreshCommand(NextMeetingService service)
    {
        _service = service;
        Name = "Refresh calendar";
        Icon = new IconInfo("\uE72C"); // Refresh
    }

    public override CommandResult Invoke()
    {
        _service.RefreshNow();
        return CommandResult.KeepOpen();
    }
}

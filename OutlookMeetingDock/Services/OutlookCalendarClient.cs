namespace OutlookMeetingDock;

/// <summary>
/// Reads today's calendar from the local Outlook profile via COM interop
/// (late binding through <see langword="dynamic"/> — no PIA reference needed,
/// so this works on .NET 10). Outlook is a COM singleton: this attaches to a
/// running instance or starts a hidden one. Works with any backend the
/// profile is connected to, including on-premises Exchange 2019.
/// </summary>
internal sealed class OutlookCalendarClient
{
    private const int OlFolderCalendar = 9;
    private const int OlMeetingCanceled = 5;
    private const int OlFree = 0;

    /// <summary>
    /// Tracks a COM RCW obtained through <see langword="dynamic"/> so it can
    /// be released in the caller's <c>finally</c> block. Every poll used to
    /// leak the whole Outlook object graph (app → session → calendar →
    /// items → per-item RCWs) — Windows eventually flagged the process with
    /// a RADAR_PRE_LEAK report, so releasing them is not optional.
    /// </summary>
    private static T Track<T>(List<object> tracked, T obj)
    {
        if (obj is object o && System.Runtime.InteropServices.Marshal.IsComObject(o))
        {
            tracked.Add(o);
        }

        return obj;
    }

    private static void ReleaseAll(IEnumerable<object> tracked)
    {
        foreach (var o in tracked)
        {
            try
            {
                _ = System.Runtime.InteropServices.Marshal.FinalReleaseComObject(o);
            }
            catch
            {
                // Already detached/disposed — nothing to do.
            }
        }
    }

    /// <summary>
    /// Returns today's meetings (already started ones included), sorted by
    /// start time. The Zoom join URL is taken from the location when present
    /// there, else from the body (some organizers paste the link only in the
    /// body — that read is safe when Outlook's object-model guard is
    /// inactive, which is the case with a healthy corporate AV; failures are
    /// swallowed so a guarded Outlook never breaks the poll).
    /// </summary>
    public IReadOnlyList<MeetingInfo> GetTodaysMeetings(bool includeAllDay)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Outlook is not installed on this machine");

        dynamic app = Activator.CreateInstance(outlookType)!;
        var tracked = new List<object> { (object)app };
        try
        {
            dynamic session = Track(tracked, app.Session);
            dynamic calendar = Track(tracked, session.GetDefaultFolder(OlFolderCalendar));
            dynamic items = Track(tracked, calendar.Items);

            // Standard pattern to expand recurring appointments: sort ascending on
            // Start, then enable recurrence expansion, then stop enumerating as
            // soon as an occurrence starts after midnight.
            items.Sort("[Start]");
            items.IncludeRecurrences = true;

            var dayStart = DateTime.Today;
            var dayEnd = dayStart.AddDays(1);
            var meetings = new List<MeetingInfo>();

            foreach (dynamic rawItem in items)
            {
                dynamic item = Track(tracked, rawItem);
                DateTime start = item.Start;
            if (start >= dayEnd)
            {
                break;
            }

            DateTime end = item.End;
            if (end <= dayStart)
            {
                continue; // ended before today (spans from yesterday)
            }

            if ((int)item.MeetingStatus == OlMeetingCanceled)
            {
                continue;
            }

            var allDay = (bool)item.AllDayEvent;
            if (allDay && !includeAllDay)
            {
                continue;
            }

            if (!allDay && (int)item.BusyStatus == OlFree)
            {
                continue;
            }

            var location = (string?)item.Location ?? string.Empty;

            // Zoom link: from the location when present there ("Room; https://…"),
            // else from the body of the invitation.
            var zoomLink = MeetingInfo.MatchZoomUrl(location);
            if (zoomLink is null)
            {
                try
                {
                    zoomLink = MeetingInfo.MatchZoomUrl((string)item.Body);
                }
                catch
                {
                    // Object-model guard active or Outlook blocked — the
                    // meeting simply has no join link then.
                }
            }

            meetings.Add(new MeetingInfo
            {
                Subject = (string?)item.Subject is { Length: > 0 } subject ? subject : "(no title)",
                Location = location,
                Start = start,
                End = end,
                AllDay = allDay,
                ZoomLink = zoomLink,
            });
        }

        // The same recurring meeting frequently exists several times over
        // (series re-invites accumulate in corporate calendars) — keep one
        // instance per subject + start.
            return [.. meetings
                .GroupBy(m => m.Key)
                .Select(g => g.First())
                .OrderBy(m => m.Start)];
        }
        finally
        {
            ReleaseAll(tracked);
        }
    }

    /// <summary>Opens the meeting's inspector window in Outlook, matched by subject + start.</summary>
    public void DisplayMeeting(MeetingInfo meeting)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Outlook is not installed on this machine");

        dynamic app = Activator.CreateInstance(outlookType)!;
        var tracked = new List<object> { (object)app };
        try
        {
            dynamic session = Track(tracked, app.Session);
            dynamic calendar = Track(tracked, session.GetDefaultFolder(OlFolderCalendar));
            dynamic items = Track(tracked, calendar.Items);
            items.Sort("[Start]");
            items.IncludeRecurrences = true;

            var dayEnd = DateTime.Today.AddDays(1);
            foreach (dynamic rawItem in items)
            {
                dynamic item = Track(tracked, rawItem);
                DateTime start = item.Start;
                if (start >= dayEnd)
                {
                    break;
                }

                if (start == meeting.Start && (string?)item.Subject == meeting.Subject)
                {
                    item.Display();
                    return;
                }
            }

            throw new InvalidOperationException($"Meeting '{meeting.Subject}' not found in the calendar");
        }
        finally
        {
            ReleaseAll(tracked);
        }
    }
}

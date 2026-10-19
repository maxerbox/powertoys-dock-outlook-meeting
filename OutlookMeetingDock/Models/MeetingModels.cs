using System.Text.RegularExpressions;

namespace OutlookMeetingDock;

/// <summary>A calendar appointment for the current day, read from the Outlook profile.</summary>
internal sealed record MeetingInfo
{
    // Matches join URLs in the Location field, corporate subdomains included:
    // "https://company.zoom.us/j/12345678901?pwd=abc…". The query stops at ';'
    // because Outlook separates room and URL with semicolons.
    private static readonly Regex ZoomUrlRegex = new(
        @"https?://(?:[\w-]+\.)?zoom\.us/j/(\d+)(\?[^;\s]*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public required string Subject { get; init; }

    /// <summary>Room / location as typed in the invitation. May be empty.</summary>
    public string Location { get; init; } = string.Empty;

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public bool AllDay { get; init; }

    public bool IsInProgress(DateTime now) => now >= Start && now < End;

    public string TimeRange => $"{Start:HH:mm}–{End:HH:mm}";

    /// <summary>
    /// Just the room name. Outlook's Location often concatenates the room with
    /// online-meeting URLs ("Room 3-05; https://company.zoom.us/j/…") — only
    /// the first segment is shown in the UI.
    /// </summary>
    public string Room
    {
        get
        {
            var first = Location.Split(';')[0].Trim();
            return first;
        }
    }

    /// <summary>Stable key used to remember for which meeting a warning was already shown.</summary>
    public string Key => $"{Subject}|{Start:yyyyMMddHHmmss}";

    /// <summary>
    /// Zoom join URL, wherever it was found: the location field
    /// ("Room; https://…zoom.us/j/…?pwd=…") or, as a fallback, the body of
    /// the invitation (some organizers paste the link only there). Set by
    /// the calendar reader; null when the meeting is not on Zoom.
    /// </summary>
    public string? ZoomLink { get; init; }

    /// <summary>The Zoom join URL, or null when the meeting is not on Zoom.</summary>
    public string? ZoomUrl => ZoomLink ?? MatchZoomUrl(Location);

    internal static string? MatchZoomUrl(string text) =>
        ZoomUrlRegex.Match(text) is { Success: true } match ? match.Value : null;

    /// <summary>
    /// zoommtg:// link that joins the meeting directly in the installed Zoom
    /// client (no browser hop): "zoommtg://zoom.us/join?confno=…&amp;pwd=…".
    /// </summary>
    public string? ZoomProtocolLink
    {
        get
        {
            if (ZoomUrl is not { } url || ZoomUrlRegex.Match(url) is not { Success: true } match)
            {
                return null;
            }

            var link = $"zoommtg://zoom.us/join?confno={match.Groups[1].Value}";
            if (match.Groups[2].Value is ['?', .. var rest])
            {
                var pwd = rest.Split('&').FirstOrDefault(p => p.StartsWith("pwd=", StringComparison.OrdinalIgnoreCase));
                if (pwd is not null)
                {
                    link += $"&{pwd}";
                }
            }

            return link;
        }
    }
}

/// <summary>Snapshot of today's calendar (or an error message when Outlook could not be reached).</summary>
internal sealed record CalendarState
{
    public static readonly CalendarState Loading = new() { Meetings = [], Error = "Loading calendar…" };

    public required IReadOnlyList<MeetingInfo> Meetings { get; init; }

    public string? Error { get; init; }

    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The meeting happening right now, if any (all-day events excluded).</summary>
    public MeetingInfo? Current(DateTime now) => Meetings.FirstOrDefault(m => !m.AllDay && m.IsInProgress(now));

    /// <summary>The next upcoming meeting, if any.</summary>
    public MeetingInfo? Next(DateTime now) => Meetings.FirstOrDefault(m => m.Start > now && !m.AllDay);
}

internal static class MeetingFormat
{
    /// <summary>Compact countdown for the dock title: "47m" / "1h05". Rounded, never truncated.</summary>
    public static string Countdown(TimeSpan t)
    {
        if (t <= TimeSpan.Zero)
        {
            return "now";
        }

        var totalMinutes = (int)Math.Round(t.TotalMinutes);
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}h{totalMinutes % 60:D2}"
            : $"{totalMinutes}m";
    }

    /// <summary>Shortens a subject so dock labels don't get ellipsed.</summary>
    public static string Abbreviate(string subject, int maxLength = 14)
    {
        var trimmed = subject.Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..(maxLength - 1)]}…";
    }
}

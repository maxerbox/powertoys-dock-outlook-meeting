namespace OutlookMeetingDock;

/// <summary>
/// Polls the Outlook calendar on a timer, keeps the current snapshot and
/// ticks every 30 s so countdowns in the Dock stay fresh. Raises
/// <see cref="WarningRaised"/> exactly once per meeting when it crosses the
/// warning threshold (default: 15 minutes before start).
/// </summary>
internal sealed class NextMeetingService : IDisposable
{
    private readonly OutlookCalendarClient _client = new();
    private readonly SettingsManager _settings;
    private readonly object _lock = new();
    private Timer? _pollTimer;
    private Timer? _tickTimer;
    private CalendarState _state = CalendarState.Loading;
    private string? _warnedKey;
    private int _pollInProgress;

    /// <summary>Raised whenever the calendar snapshot or the countdown display should refresh.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised once per meeting when it enters the warning window.</summary>
    public event EventHandler<MeetingInfo>? WarningRaised;

    public NextMeetingService(SettingsManager settings)
    {
        _settings = settings;
        _settings.SettingsUpdated += OnSettingsUpdated;
        ResetTimers();
        _ = PollAsync();
    }

    public CalendarState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public void RefreshNow() => _ = PollAsync();

    public void Dispose()
    {
        _settings.SettingsUpdated -= OnSettingsUpdated;
        _pollTimer?.Dispose();
        _tickTimer?.Dispose();
    }

    private void OnSettingsUpdated(object? sender, EventArgs e)
    {
        ResetTimers();
        RefreshNow();
    }

    private void ResetTimers()
    {
        lock (_lock)
        {
            var pollInterval = TimeSpan.FromMinutes(Math.Max(1, _settings.PollMinutes));
            _pollTimer?.Dispose();
            _pollTimer = new Timer(_ => RefreshNow(), null, pollInterval, pollInterval);

            // Fast tick so the dock countdown stays accurate to ~30 s.
            _tickTimer?.Dispose();
            _tickTimer = new Timer(_ => OnTick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    private async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _pollInProgress, 1) == 1)
        {
            return; // a poll is already in flight
        }

        try
        {
            var includeAllDay = _settings.IncludeAllDay;

            // COM calls into Outlook can block indefinitely (Outlook starting
            // up, busy, or a modal dialog). A hung COM call previously froze
            // the extension process, which then crashed the whole palette
            // host. Enforce a hard timeout: on expiry the extension stays
            // responsive and shows an error state instead.
            var meetings = await Task.Run(() => _client.GetTodaysMeetings(includeAllDay))
                .WaitAsync(TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            lock (_lock)
            {
                _state = new CalendarState { Meetings = meetings };
            }
        }
        catch (TimeoutException)
        {
            lock (_lock)
            {
                _state = new CalendarState { Meetings = [], Error = "Outlook did not respond in time (is it busy or showing a dialog?)" };
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = new CalendarState { Meetings = [], Error = ex.Message };
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pollInProgress, 0);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        CalendarState state;
        lock (_lock)
        {
            state = _state;
        }

        // Threshold-crossing detection: warn exactly once per meeting.
        if (state.Error is null && state.Next(now) is { } next)
        {
            var minutesToStart = (next.Start - now).TotalMinutes;
            if (minutesToStart > 0 && minutesToStart <= _settings.WarnMinutes && _warnedKey != next.Key)
            {
                _warnedKey = next.Key;
                WarningRaised?.Invoke(this, next);
            }
        }

        // Always let the dock re-render the countdown.
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

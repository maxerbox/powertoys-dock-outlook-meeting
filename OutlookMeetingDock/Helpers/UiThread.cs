using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace OutlookMeetingDock;

/// <summary>
/// Single dedicated thread that serializes page notifications. The calendar
/// poll pipeline completes on threadpool threads (and the countdown ticks on
/// a <c>System.Threading.Timer</c> every 30 s) while the palette calls
/// <c>GetItems()</c> on its own COM RPC threads. <see cref="Post"/> is the
/// ONLY safe operation: <c>RaiseItemsChanged</c> is a synchronous COM call
/// into the host, and the host re-enters <c>GetItems</c> from that event —
/// so a <c>GetItems</c> that blocks waiting for this thread (as a queued
/// <see cref="Invoke"/> would) deadlocks and the dock band stays on
/// "loading...". Pages therefore guard their state with a plain lock and
/// only use this thread to raise change notifications off the poll
/// pipeline.
/// </summary>
internal static class UiThread
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Thread Thread = Start();

    private static Thread Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "OutlookMeetingDock.PageThread",
        };
        thread.Start();
        return thread;
    }

    private static void Run()
    {
        foreach (var action in Queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                // Keep the pump alive no matter what a page throws.
            }
        }
    }

    /// <summary>Queues <paramref name="action"/> on the page thread without blocking the caller.</summary>
    public static void Post(Action action) => Queue.Add(action);
}

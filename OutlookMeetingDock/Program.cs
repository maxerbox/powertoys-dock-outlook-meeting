using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace OutlookMeetingDock;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // Single extension instance returned every time the host asks for the IExtension object.
            OutlookMeetingDockExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<OutlookMeetingDockExtension, IExtension>(() => extensionInstance);
            server.Start();

            // Wait until the host disposes the extension, then shut down.
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as an Extension... exiting.");
        }
    }
}

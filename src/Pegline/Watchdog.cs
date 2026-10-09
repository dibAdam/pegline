using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace Pegline
{
    /// <summary>
    /// Development only (PEGLINE_FRAMES=1): notices when the UI thread stops
    /// answering for more than 150 ms and writes where it was to the log, so
    /// a stutter can be traced to its line of code.
    /// </summary>
    static class Watchdog
    {
        public static void Start(Dispatcher dispatcher)
        {
            var ui = Thread.CurrentThread;
            long answered = Stopwatch.GetTimestamp();
            bool reported = false;
            var thread = new Thread(() =>
            {
                while (true)
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => Interlocked.Exchange(ref answered, Stopwatch.GetTimestamp())));
                    Thread.Sleep(40);
                    double waited = (Stopwatch.GetTimestamp() - Interlocked.Read(ref answered)) * 1000.0 / Stopwatch.Frequency;
                    if (waited > 150 && !reported)
                    {
                        reported = true;
                        StackTrace trace = null;
#pragma warning disable 618
                        try
                        {
                            ui.Suspend();
                            trace = new StackTrace(ui, false);
                        }
                        catch { }
                        finally
                        {
                            try { ui.Resume(); } catch { }
                        }
#pragma warning restore 618
                        Log.Info($"UI thread blocked for {waited:0} ms, at:\r\n{trace}");
                    }
                    else if (waited < 60)
                    {
                        reported = false;
                    }
                }
            }) { IsBackground = true, Name = "Watchdog" };
            thread.Start();
        }
    }
}

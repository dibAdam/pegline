using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Pegline
{
    sealed class Options
    {
        /// <summary>Watch this folder instead of the Screenshots folder, for this run only.</summary>
        public string Folder;
        /// <summary>Started from the login item.</summary>
        public bool Startup;
        /// <summary>No first-run offer or welcome, for automated runs.</summary>
        public bool Quiet;
        public bool Quit;
        /// <summary>Render on the GPU instead of the CPU.</summary>
        public bool Gpu;
        /// <summary>Development: open the tray menu in the middle of the main display, for screenshots.</summary>
        public bool PreviewMenu;
        /// <summary>Development: keep the line down and pluck it and blow on it every couple of seconds.</summary>
        public bool Demo;
        /// <summary>Light the fairy lights whatever the time.</summary>
        public bool Night;
        /// <summary>Development: pin the first photo the way the menu does, for testing.</summary>
        public bool PreviewPin;
        /// <summary>Development: show one part of the app: settings, tour, editor, preview, keyboard, undo or tab.</summary>
        public string Preview;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--folder" when i + 1 < args.Length: o.Folder = args[++i]; break;
                    case "--profile" when i + 1 < args.Length: Settings.UseProfile(args[++i]); break;
                    case "--startup": o.Startup = true; break;
                    case "--quiet": o.Quiet = true; break;
                    case "--quit": o.Quit = true; break;
                    case "--gpu": o.Gpu = true; break;
                    case "--preview-menu": o.PreviewMenu = true; break;
                    case "--demo": o.Demo = true; break;
                    case "--night": o.Night = true; break;
                    case "--preview-pin": o.PreviewPin = true; break;
                    case "--preview" when i + 1 < args.Length: o.Preview = args[++i].ToLowerInvariant(); break;
                }
            }
            return o;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var options = Parse(args);
            string name = @"Local\" + Settings.Profile;

            using (var single = new Mutex(true, name + "-instance", out bool first))
            {
                if (!first)
                {
                    // Already running: ask that copy to show the line, or to quit.
                    Signal(name + (options.Quit ? "-quit" : "-show"));
                    return;
                }
                if (options.Quit) return;

                if (Environment.GetEnvironmentVariable("PEGLINE_THROTTLED") != "1") Native.RunAtFullSpeed();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log.Error("Unhandled", e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Fatal", e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    Log.Error("Unobserved", e.Exception);
                    e.SetObserved();
                };

                // Every window here is a layered, transparent one, which WPF has
                // to copy back from the GPU each frame anyway. Drawing them on the
                // CPU is as smooth and spares the 150 MB or more that the graphics
                // driver otherwise keeps after the first full screen flight.
                if (!options.Gpu)
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                Theme.Initialize(app);
                if (Environment.GetEnvironmentVariable("PEGLINE_FRAMES") == "1") Watchdog.Start(app.Dispatcher);
                var controller = new Controller(options);
                Listen(name + "-show", controller.ShowFromOutside);
                Listen(name + "-quit", controller.Quit);
                app.Startup += (s, e) => controller.Start();
                app.Run();
                GC.KeepAlive(single);
            }
        }

        static Options Parse(string[] args) => Options.Parse(args);

        static void Signal(string name)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(name, out var handle))
                {
                    handle.Set();
                    handle.Dispose();
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not reach the running copy", e);
            }
        }

        static void Listen(string name, Action action)
        {
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var dispatcher = Dispatcher.CurrentDispatcher;
            ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) =>
                dispatcher.BeginInvoke(action), null, Timeout.Infinite, false);
            GC.KeepAlive(handle);
            handles = handles ?? new System.Collections.Generic.List<EventWaitHandle>();
            handles.Add(handle);
        }

        static System.Collections.Generic.List<EventWaitHandle> handles;
    }
}

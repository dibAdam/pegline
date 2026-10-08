using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>One display, in physical pixels, with its own scale.</summary>
    sealed class MonitorInfo
    {
        public IntPtr Handle { get; }
        public string Device { get; }
        public PxRect Bounds { get; }
        public PxRect WorkArea { get; }
        public double Scale { get; }
        public bool Primary { get; }

        public MonitorInfo(IntPtr handle, string device, PxRect bounds, PxRect workArea, double scale, bool primary)
        {
            Handle = handle; Device = device; Bounds = bounds; WorkArea = workArea; Scale = scale; Primary = primary;
        }

        public bool SameAs(MonitorInfo other) => other != null && other.Device == Device;
        public override string ToString() => $"{Device} {Bounds} x{Scale:0.##}";
    }

    static class Monitors
    {
        static List<MonitorInfo> cache;
        static DateTime cachedAt;

        /// <summary>Cached briefly: the pointer is checked 30 times a second.</summary>
        public static IReadOnlyList<MonitorInfo> All
        {
            get
            {
                if (cache == null || (DateTime.UtcNow - cachedAt).TotalSeconds > 2) Refresh();
                return cache;
            }
        }

        public static void Refresh()
        {
            var list = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr data) =>
            {
                var m = Describe(h);
                if (m != null) list.Add(m);
                return true;
            }, IntPtr.Zero);
            cache = list;
            cachedAt = DateTime.UtcNow;
        }

        static MonitorInfo Describe(IntPtr handle)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(handle, ref info)) return null;
            double scale = 1;
            try
            {
                if (GetDpiForMonitor(handle, 0, out uint dpi, out _) == 0 && dpi > 0) scale = dpi / 96.0;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return new MonitorInfo(handle, info.szDevice, PxRect.From(info.rcMonitor), PxRect.From(info.rcWork),
                                   scale, (info.dwFlags & 1) != 0);
        }

        public static MonitorInfo At(POINT p)
        {
            foreach (var m in All)
                if (m.Bounds.Contains(p)) return m;
            var h = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
            return All.FirstOrDefault(m => m.Handle == h) ?? Primary;
        }

        public static MonitorInfo UnderCursor() => At(Cursor());

        public static MonitorInfo Primary => All.FirstOrDefault(m => m.Primary) ?? All.FirstOrDefault();

        public static MonitorInfo Find(string device) =>
            device == null ? null : All.FirstOrDefault(m => m.Device == device);
    }
}

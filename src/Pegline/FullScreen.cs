using System;
using System.Diagnostics;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>
    /// Knows when a display is showing something full screen, like a video, a
    /// game or a slideshow. The line never comes down over those.
    /// </summary>
    static class FullScreen
    {
        static readonly uint ownProcess = (uint)Process.GetCurrentProcess().Id;

        public static bool IsActive(MonitorInfo monitor)
        {
            if (monitor == null) return false;

            // Exclusive full screen games and presentation mode.
            try
            {
                if (SHQueryUserNotificationState(out int state) == 0
                    && (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                {
                    var fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && FrameBounds(fg).Intersects(monitor.Bounds)) return true;
                }
            }
            catch { }

            // Otherwise: the topmost large window on that display covers all of
            // it and has no title bar. A maximized window keeps its title bar,
            // even with the taskbar set to hide, so it does not count.
            bool result = false;
            var screen = monitor.Bounds;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
                if (ProcessOf(h) == ownProcess) return true;

                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0) return true;

                var cls = ClassName(h);
                if (cls == "Progman" || cls == "WorkerW") return false; // reached the desktop
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;

                var frame = FrameBounds(h);
                var overlap = frame.Intersect(screen);
                // Small floating windows, like a picture-in-picture or a call
                // toolbar, sit on top without deciding anything.
                if (overlap.Area < screen.Area / 2) return true;

                bool covers = frame.Contains(screen);
                bool titled = (GetWindowLong(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
                result = covers && !titled;
                return false;
            }, IntPtr.Zero);
            return result;
        }
    }

    /// <summary>
    /// Works out where on screen a capture was taken, so it can fly from there
    /// to the line. Windows keeps no record of it, unlike macOS, so the size of
    /// the image is matched against what was under the pointer.
    /// </summary>
    static class CaptureGuess
    {
        /// <param name="nearCapture">A capture tool just reported a snip, so the pointer is where the snip ended.</param>
        public static PxRect? Guess(int width, int height, POINT cursor, bool nearCapture)
        {
            // A whole display.
            PxRect? display = null;
            foreach (var m in Monitors.All)
            {
                if (m.Bounds.Width != width || m.Bounds.Height != height) continue;
                if (display == null || m.Bounds.Contains(cursor)) display = m.Bounds;
            }
            if (display != null) return display;
            if (!nearCapture) return null;

            var screen = Monitors.At(cursor);
            if (screen == null) return null;

            // A window: the snip is exactly the window under the pointer.
            var root = GetAncestor(WindowFromPoint(cursor), GA_ROOT);
            if (root != IntPtr.Zero && ProcessOf(root) != (uint)Process.GetCurrentProcess().Id)
            {
                var frame = FrameBounds(root);
                if (Math.Abs(frame.Width - width) <= 2 && Math.Abs(frame.Height - height) <= 2) return frame;
            }

            // A rectangle: the pointer was released on one of its corners,
            // most often the bottom right one.
            var candidates = new[]
            {
                new PxRect(cursor.X - width, cursor.Y - height, width, height),
                new PxRect(cursor.X, cursor.Y, width, height),
                new PxRect(cursor.X - width, cursor.Y, width, height),
                new PxRect(cursor.X, cursor.Y - height, width, height),
            };
            foreach (var r in candidates)
                if (screen.Bounds.Contains(r)) return r;
            return null;
        }
    }
}

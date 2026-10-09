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
        static string cachedDevice;
        static bool cachedResult;
        static DateTime cachedAt;

        /// <summary>
        /// Asked many times a second while the pointer rests at the top edge,
        /// so an answer is reused for a quarter of a second.
        /// </summary>
        public static bool IsActive(MonitorInfo monitor)
        {
            if (monitor == null) return false;
            var now = DateTime.UtcNow;
            if (monitor.Device == cachedDevice && (now - cachedAt).TotalSeconds < 0.25) return cachedResult;
            cachedResult = Check(monitor);
            cachedDevice = monitor.Device;
            cachedAt = now;
            return cachedResult;
        }

        static bool Check(MonitorInfo monitor)
        {
            // Exclusive full screen games and presentation mode.
            try
            {
                if (Animator.Timed("notification state", () => SHQueryUserNotificationState(out int st) == 0 ? st : -1) is int state && state >= 0
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
                // Cheap questions first. Asking the window manager (cloaking,
                // the visible frame) costs a round trip per window, so only the
                // few windows that could matter get asked.
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0) return true;
                GetWindowRect(h, out RECT rough);
                if (PxRect.From(rough).Intersect(screen).Area < screen.Area / 2) return true;
                if (ProcessOf(h) == ownProcess) return true;

                var cls = ClassName(h);
                if (cls == "Progman" || cls == "WorkerW") return false; // reached the desktop
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                if (IsCloaked(h)) return true;

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

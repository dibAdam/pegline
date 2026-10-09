using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WF = System.Windows.Forms;

namespace Pegline
{
    /// <summary>
    /// The notification area icon. Left click shows or hides the line, the
    /// way the menu bar item does on macOS; right click opens the menu.
    /// </summary>
    sealed class Tray : IDisposable
    {
        readonly WF.NotifyIcon icon;
        Icon image;

        public Tray(Action onClick, Func<ContextMenu> menu)
        {
            icon = new WF.NotifyIcon { Text = "Pegline" };
            UpdateImage();
            icon.MouseUp += (s, e) =>
            {
                if (e.Button == WF.MouseButtons.Left) onClick();
                else if (e.Button == WF.MouseButtons.Right) Menus.Show(menu());
            };
            icon.Visible = true;
            Theme.Changed += UpdateImage;
        }

        public void UpdateImage()
        {
            double scale = Monitors.Primary?.Scale ?? 1;
            int size;
            try { size = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, (uint)Math.Round(96 * scale)); }
            catch (EntryPointNotFoundException) { size = (int)Math.Round(16 * scale); }
            var next = TrayGlyph.Create(Math.Max(16, size), white: Theme.TaskbarDark);
            icon.Icon = next;
            image?.Dispose();
            image = next;
        }

        /// <summary>A Windows notification from the tray icon, pointing at it.</summary>
        public void ShowTip(string title, string text)
        {
            try { icon.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.None); }
            catch (Exception e) { Log.Error("Could not show a notification", e); }
        }

        public void Dispose()
        {
            Theme.Changed -= UpdateImage;
            icon.Visible = false;
            icon.Dispose();
            image?.Dispose();
        }
    }

    /// <summary>
    /// A monochrome glyph in the style of the system tray icons: a line with
    /// two frames hanging from it. Drawn at the exact pixel size, so it stays
    /// crisp at every scale.
    /// </summary>
    static class TrayGlyph
    {
        public static Icon Create(int size, bool white)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float s = size / 16f;
                    var color = white ? Color.White : Color.FromArgb(0x1C, 0x1C, 0x1C);
                    using (var pen = new Pen(color, Math.Max(1f, (float)Math.Round(s))))
                    using (var fill = new SolidBrush(color))
                    {
                        g.DrawBezier(pen, -1, 3.2f * s, 5 * s, 5.2f * s, 11 * s, 5.2f * s, 17 * s, 3.2f * s);
                        DrawRounded(g, pen, new RectangleF(1.5f * s, 5.5f * s, 6 * s, 7 * s), 1.4f * s);
                        DrawRounded(g, pen, new RectangleF(8.5f * s, 5.5f * s, 6 * s, 5 * s), 1.4f * s);
                        g.FillRectangle(fill, new RectangleF(3.6f * s, 2.6f * s, 1.8f * s, 4.2f * s));
                        g.FillRectangle(fill, new RectangleF(10.6f * s, 2.6f * s, 1.8f * s, 4.2f * s));
                    }
                }
                var handle = bmp.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(handle)) return (Icon)temp.Clone();
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }

        static void DrawRounded(Graphics g, Pen pen, RectangleF r, float radius)
        {
            using (var path = new GraphicsPath())
            {
                float d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }
    }

    static class Menus
    {
        public static MenuItem Item(string title, Action action, bool enabled = true, bool? check = null,
                                    string gesture = null, string tip = null)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled };
            if (check.HasValue) item.IsChecked = check.Value;
            if (gesture != null) item.InputGestureText = gesture;
            if (tip != null) item.ToolTip = tip;
            // Run once the menu has closed, so dialogs open cleanly.
            item.Click += (s, e) => item.Dispatcher.BeginInvoke(action);
            return item;
        }

        /// <summary>
        /// Opens a menu at the pointer. A menu from a background app only closes
        /// on an outside click when its window is in the foreground, so it is
        /// brought there, the way Windows asks tray apps to do.
        /// </summary>
        public static void Show(ContextMenu menu, System.Windows.Point? at = null)
        {
            menu.Placement = at.HasValue ? PlacementMode.AbsolutePoint : PlacementMode.MousePoint;
            if (at.HasValue)
            {
                menu.HorizontalOffset = at.Value.X;
                menu.VerticalOffset = at.Value.Y;
            }
            menu.Opened += (s, e) =>
            {
                if (System.Windows.PresentationSource.FromVisual(menu) is HwndSource source)
                    Native.SetForegroundWindow(source.Handle);
                menu.Focus();
            };
            menu.IsOpen = true;
        }
    }
}

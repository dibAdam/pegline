using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>Measurements of the line, in device-independent pixels. The same as the macOS original.</summary>
    static class Layout
    {
        public const double PanelHeight = 210;
        public const double RopeTop = 10;
        public const double Spacing = 174;
        public const double CardWidth = 150;
        public const double PinAbove = 9.5;
        public const double PhotoMaxHeight = 104;
        public const double FrameRadius = 16;
        public const double FrameInset = 4;
        /// <summary>Distance from the top of the hanging view (the clip) to the card: the clip is 26 tall, 12 of them over the card.</summary>
        public const double CardOffsetBelowTop = 26 - 12;

        public static double X(int index, int count, double width)
        {
            double total = Math.Max(count - 1, 0) * Spacing;
            return width / 2 - total / 2 + index * Spacing;
        }

        /// <summary>The photo fits inside the card area keeping its proportions, so the frame hugs it whether the screenshot is wide or tall.</summary>
        public static Size PhotoSize(double width, double height)
        {
            double maxW = CardWidth - 14, maxH = PhotoMaxHeight;
            if (width <= 0 || height <= 0) return new Size(maxW, maxH);
            double scale = Math.Min(maxW / width, maxH / height);
            return new Size(width * scale, height * scale);
        }

        public static Size CardSize(double width, double height)
        {
            var p = PhotoSize(width, height);
            return new Size(p.Width + FrameInset * 2, p.Height + FrameInset * 2);
        }
    }

    /// <summary>
    /// A transparent, always-on-top window that never takes focus, stays out
    /// of the taskbar and Alt+Tab, and is placed in physical pixels so it lands
    /// exactly where it should on displays with different scales.
    /// </summary>
    class OverlayWindow : Window
    {
        PxRect bounds;
        bool hasBounds;
        bool clickThrough;

        public IntPtr Handle { get; private set; }

        public OverlayWindow(bool clickThrough)
        {
            this.clickThrough = clickThrough;
            Title = "Pegline";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            // Kept out of the taskbar by the tool window style below, not by
            // ShowInTaskbar = false: for that, WPF gives the window a hidden
            // owner that is not topmost, and the window then sinks behind
            // ordinary windows as they are activated.
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            SourceInitialized += (s, e) =>
            {
                Handle = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
                if (this.clickThrough) ex |= WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
            };
        }

        public PxRect PixelBounds => bounds;

        public double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        public void SetPixelBounds(PxRect r)
        {
            if (Handle == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            bounds = r;
            hasBounds = true;
            ApplyBounds();
        }

        /// <summary>Moves the window without resizing it: nothing has to be drawn again.</summary>
        public void MoveTo(int x, int y)
        {
            if (Handle == IntPtr.Zero) return;
            bounds = new PxRect(x, y, bounds.Width, bounds.Height);
            SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public void ApplyBounds()
        {
            if (hasBounds && Handle != IntPtr.Zero)
                SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            // Windows proposes a size for the new scale; keep the one we asked for.
            if (hasBounds) Dispatcher.BeginInvoke(new Action(ApplyBounds), DispatcherPriority.Send);
        }

        /// <summary>Whether the window may take focus, for keyboard use. Never by default.</summary>
        public bool Activatable
        {
            set
            {
                if (Handle == IntPtr.Zero) return;
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                SetWindowLong(Handle, GWL_EXSTYLE, value ? ex & ~WS_EX_NOACTIVATE : ex | WS_EX_NOACTIVATE);
            }
        }

        /// <summary>Clicks pass through to whatever is underneath.</summary>
        public bool ClickThrough
        {
            get => clickThrough;
            set
            {
                if (value == clickThrough) return;
                clickThrough = value;
                if (Handle == IntPtr.Zero) return;
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                SetWindowLong(Handle, GWL_EXSTYLE, value ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
            }
        }

        public Point ToLocal(POINT p)
        {
            double s = Scale;
            return new Point((p.X - bounds.X) / s, (p.Y - bounds.Y) / s);
        }

        public Rect ToLocal(PxRect r)
        {
            double s = Scale;
            return new Rect((r.X - bounds.X) / s, (r.Y - bounds.Y) / s, r.Width / s, r.Height / s);
        }

        public PxRect ToScreen(Rect r)
        {
            double s = Scale;
            return new PxRect((int)Math.Round(bounds.X + r.X * s), (int)Math.Round(bounds.Y + r.Y * s),
                              (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
        }
    }

    static class Visuals
    {
        public static FontFamily UiFont => (FontFamily)Application.Current.FindResource("Pl.Font");
        public static FontFamily IconFont => (FontFamily)Application.Current.FindResource("Pl.Icons");

        static readonly Brush Metal = Frozen(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(179, 179, 179), 0),
                new GradientStop(Color.FromRgb(237, 237, 237), 0.35),
                new GradientStop(Color.FromRgb(209, 209, 209), 0.65),
                new GradientStop(Color.FromRgb(158, 158, 158), 1),
            }
        });

        static readonly Brush MetalEdge = Frozen(new LinearGradientBrush(
            Color.FromArgb(230, 255, 255, 255), Color.FromArgb(46, 0, 0, 0), 90));

        static readonly Brush Slot = Frozen(new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)));

        /// <summary>
        /// A minimal aluminium clip: a brushed metal pill with a slot where it
        /// grips the line, and a soft shadow so it reads on any background.
        /// </summary>
        public static FrameworkElement Clothespin()
        {
            var pill = new Border
            {
                Width = 9,
                Height = 26,
                CornerRadius = new CornerRadius(3.5),
                Background = Metal,
                BorderBrush = MetalEdge,
                BorderThickness = new Thickness(0.6)
            };
            var slot = new Border
            {
                Width = 5,
                Height = 1.4,
                CornerRadius = new CornerRadius(0.7),
                Background = Slot,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8.5, 0, 0)
            };
            return new Grid
            {
                Width = 9,
                Height = 26,
                IsHitTestVisible = false,
                Children = { pill, slot },
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.32, BlurRadius = 4, ShadowDepth = 1.5, Direction = 270 }
            };
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

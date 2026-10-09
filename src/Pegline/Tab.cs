using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>
    /// The small tab hanging from the top edge while photos wait on a tucked
    /// line, with how many there are. Click it, or rest the pointer on it, and
    /// the line comes down. It is its own little window, so it stays put while
    /// the line slides.
    /// </summary>
    sealed class TabWindow : OverlayWindow
    {
        /// <summary>Resting on the tab opens the line too.</summary>
        const double RestDelay = 0.25;
        const double Room = 10;

        readonly Border tab;
        readonly TextBlock count;
        readonly Motion fade;
        readonly DispatcherTimer rest;
        bool wanted;

        public event Action Opened;

        public TabWindow() : base(clickThrough: false)
        {
            count = new TextBlock
            {
                FontFamily = Visuals.UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.Primary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 1)
            };
            var chevron = new TextBlock
            {
                Text = "",
                FontFamily = Visuals.IconFont,
                FontSize = 8,
                Foreground = Palette.Secondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            var pin = new Border
            {
                Width = 4,
                Height = 10,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(Color.FromRgb(232, 232, 232), Color.FromRgb(160, 160, 160), 0)
            };
            tab = new Border
            {
                Height = 20,
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Padding = new Thickness(11, 0, 10, 1),
                Margin = new Thickness(Room, 0, Room, Room),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75, 0, 0.75, 0.75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                ToolTip = L("Show the line", "Mostrar el tendedero", "Afficher le fil"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { pin, count, chevron } },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.3 }
            };
            Content = tab;
            fade = new Motion(0, v => Opacity = v, 0.002);
            rest = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RestDelay) };
            rest.Tick += (s, e) =>
            {
                rest.Stop();
                if (wanted) Opened?.Invoke();
            };
            tab.MouseEnter += (s, e) => rest.Start();
            tab.MouseLeave += (s, e) => rest.Stop();
            tab.MouseLeftButtonUp += (s, e) =>
            {
                rest.Stop();
                Opened?.Invoke();
            };
        }

        public void Update(bool show, int photos, MonitorInfo monitor)
        {
            count.Text = photos.ToString();
            if (show && monitor != null)
            {
                tab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double s = monitor.Scale;
                int w = (int)Math.Ceiling(tab.DesiredSize.Width * s), h = (int)Math.Ceiling(tab.DesiredSize.Height * s);
                var work = monitor.WorkArea;
                var target = new PxRect(work.X + (work.Width - w) / 2, work.Y, w, h);
                if (!target.Equals(PixelBounds)) SetPixelBounds(target);
            }
            if (show == wanted) return;
            wanted = show;
            if (show)
            {
                if (!IsVisible)
                {
                    Opacity = 0;
                    Show();
                    ApplyBounds();
                }
                fade.Tween(1, 0.2, Ease.Out);
            }
            else
            {
                rest.Stop();
                fade.Tween(0, 0.1, Ease.In, () => { if (!wanted) Hide(); });
            }
        }
    }
}

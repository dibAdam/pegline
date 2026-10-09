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
    /// One step back. Taking something down or throwing it away offers an
    /// Undo for a few seconds; only when that passes does anything final
    /// happen, such as a file going to the Recycle Bin. So Undo is instant and
    /// never has to fish a file out of the Recycle Bin.
    /// </summary>
    static class Undo
    {
        public const double Seconds = 6;

        static Action undo, commit;

        /// <summary>Raised with a message to show while an undo is on offer, and with null when it is gone.</summary>
        public static event Action<string> Changed;

        public static bool CanUndo => undo != null;

        /// <param name="commit">What makes it final, run when the offer runs out or a newer one replaces it.</param>
        public static void Offer(string message, Action undo, Action commit = null)
        {
            Commit();
            Undo.undo = undo;
            Undo.commit = commit;
            Changed?.Invoke(message);
        }

        public static void Perform()
        {
            var action = undo;
            undo = null;
            commit = null;
            Changed?.Invoke(null);
            try { action?.Invoke(); }
            catch (Exception e) { Log.Error("Undo failed", e); }
        }

        /// <summary>Makes the pending change final. Also called when Pegline quits.</summary>
        public static void Commit()
        {
            var action = commit;
            undo = null;
            commit = null;
            try { action?.Invoke(); }
            catch (Exception e) { Log.Error("Could not finish an undoable change", e); }
        }

        public static void Expire()
        {
            if (undo == null) return;
            Commit();
            Changed?.Invoke(null);
        }
    }

    /// <summary>
    /// The small "Undo" pill at the top of the screen. It takes a click but
    /// never focus, waits while the pointer is over it, and leaves on its own.
    /// </summary>
    sealed class UndoToast : OverlayWindow
    {
        readonly TextBlock message;
        readonly Border pill;
        readonly DispatcherTimer timer;
        readonly Motion fade;
        DateTime shownAt;

        public UndoToast() : base(clickThrough: false)
        {
            message = new TextBlock
            {
                FontFamily = Visuals.UiFont,
                FontSize = 12.5,
                Foreground = Palette.Primary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 1)
            };
            var button = new TextBlock
            {
                Text = L("Undo", "Deshacer", "Annuler"),
                FontFamily = Visuals.UiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand
            };
            button.SetResourceReference(TextBlock.ForegroundProperty, "Pl.Accent");
            pill = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(16),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { message, button } },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.28 }
            };
            Content = pill;
            pill.MouseLeftButtonUp += (s, e) => Undo.Perform();
            fade = new Motion(0, v => Opacity = v, 0.002);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                // Waits while the pointer is on it, so it never leaves under a hand reaching for it.
                if (IsMouseOver) shownAt = DateTime.UtcNow;
                else if ((DateTime.UtcNow - shownAt).TotalSeconds > Undo.Seconds) Undo.Expire();
            };
            Undo.Changed += OnChanged;
        }

        void OnChanged(string text)
        {
            if (text == null)
            {
                timer.Stop();
                fade.Tween(0, 0.18, Ease.In, Hide);
                return;
            }
            message.Text = text;
            shownAt = DateTime.UtcNow;
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var monitor = Monitors.UnderCursor() ?? Monitors.Primary;
            double s = monitor.Scale;
            int width = (int)Math.Ceiling(pill.DesiredSize.Width * s), height = (int)Math.Ceiling(pill.DesiredSize.Height * s);
            var work = monitor.WorkArea;
            // Just below where the line hangs, in the middle, clear of the photos.
            SetPixelBounds(new PxRect(work.X + (work.Width - width) / 2, work.Y + (int)((Layout.PanelHeight - 8) * s), width, height));
            Show();
            ApplyBounds();
            fade.Tween(1, 0.15, Ease.Out);
            timer.Start();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pegline
{
    /// <summary>
    /// The capture lifting off the screen and flying up to the line. It turns
    /// into the hanging card on the way: it shrinks, tilts into place and grows
    /// its glass frame and clip, so there is nothing left to change on landing.
    /// The same window draws a discarded card falling, so it is never cut off
    /// by the line's strip.
    /// </summary>
    sealed class Flight
    {
        public const double Duration = 0.65;
        /// <summary>How high the gentle arc rises halfway.</summary>
        const double Arc = 30;

        static readonly List<Flight> current = new List<Flight>();

        readonly OverlayWindow window = new OverlayWindow(clickThrough: true);
        readonly Canvas stage = new Canvas();
        readonly Grid card = new Grid();
        readonly RotateTransform rotate = new RotateTransform();
        readonly DropShadowEffect shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Direction = 270 };
        readonly Border glass, edge;
        readonly Image photo;
        readonly FrameworkElement clip;
        readonly PxRect fromPx, toPx;
        readonly double tilt;
        readonly bool falling;
        Motion progress;
        Action completion = () => { };

        /// <param name="from">The captured area, in screen pixels.</param>
        /// <param name="to">The card's frame on the line, in screen pixels, untilted.</param>
        /// <param name="tilt">The card's resting tilt in degrees, clockwise.</param>
        public static void Fly(BitmapSource image, PxRect from, PxRect to, double tilt, MonitorInfo monitor, Action completion)
        {
            var region = from.Union(to).Inflate((int)(80 * monitor.Scale), (int)(80 * monitor.Scale)).Intersect(monitor.Bounds);
            var flight = new Flight(image, from, to, tilt, region, falling: false);
            flight.completion = completion;
            flight.Run(Duration);
        }

        /// <summary>A discarded card falling off the line: 520 points down, tilting further, fading, 0.55 s ease in.</summary>
        public static void Fall(BitmapSource image, PxRect card, double tilt, MonitorInfo monitor)
        {
            double s = monitor.Scale;
            var region = PxRect.FromLTRB(card.X - (int)(220 * s), card.Y - (int)(40 * s),
                                         card.Right + (int)(220 * s), card.Bottom + (int)(560 * s)).Intersect(monitor.Bounds);
            new Flight(image, card, card, tilt, region, falling: true).Run(0.55);
        }

        Flight(BitmapSource image, PxRect from, PxRect to, double tilt, PxRect region, bool falling)
        {
            fromPx = from;
            toPx = to;
            this.tilt = tilt;
            this.falling = falling;

            glass = new Border { Background = Palette.Glass };
            photo = new Image { Source = image, Stretch = Stretch.UniformToFill };
            // It is moving; the sharper filter would only cost frames.
            RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.Linear);
            edge = new Border { BorderBrush = Palette.Edge, BorderThickness = new Thickness(0.75) };
            clip = Visuals.Clothespin();
            clip.HorizontalAlignment = HorizontalAlignment.Center;
            clip.VerticalAlignment = VerticalAlignment.Top;
            // The clip grips the top edge: 26 tall, 12 of them over the card.
            clip.Margin = new Thickness(0, -(26 - 12), 0, 0);
            card.Children.Add(glass);
            card.Children.Add(photo);
            card.Children.Add(edge);
            card.Children.Add(clip);
            card.RenderTransform = rotate;
            stage.Children.Add(card);
            window.Content = stage;
            window.SetPixelBounds(region);
        }

        void Run(double seconds)
        {
            current.Add(this);
            if (falling) { Update(1); UpdateFall(0); }
            else Update(0);
            window.Show();
            window.ApplyBounds();
            progress = new Motion(0, k => { if (falling) UpdateFall(k); else Update(k); }, 0.0001);
            progress.Tween(1, seconds, Ease.Linear, Finish);
        }

        void Finish()
        {
            completion();
            if (falling)
            {
                Close();
                return;
            }
            // The real card fades in underneath; this one fades out over it.
            var fade = new Motion(1, v => window.Opacity = v, 0.002);
            fade.Tween(0, 0.16, Ease.Linear, Close);
        }

        void Close()
        {
            window.Close();
            current.Remove(this);
        }

        void Update(double raw)
        {
            double k = Easing.Apply(Ease.InOutCubic, raw);
            double chrome = Easing.Smooth(k, 0.35, 1);
            var from = window.ToLocal(fromPx);
            var to = window.ToLocal(toPx);

            double w = Easing.Lerp(from.Width, to.Width, k), h = Easing.Lerp(from.Height, to.Height, k);
            double topX = Easing.Lerp(from.X + from.Width / 2, to.X + to.Width / 2, k);
            double topY = Easing.Lerp(from.Y, to.Y, k) - Math.Sin(Math.PI * k) * Arc;
            double inset = Layout.FrameInset * k;
            double radius = Easing.Lerp(0, Layout.FrameRadius, k);

            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, topX - w / 2);
            Canvas.SetTop(card, topY);
            rotate.CenterX = w / 2;
            rotate.CenterY = 0;
            rotate.Angle = tilt * k;

            glass.CornerRadius = new CornerRadius(radius);
            glass.Opacity = chrome;
            edge.CornerRadius = new CornerRadius(radius);
            edge.Opacity = chrome;
            double pw = Math.Max(1, w - 2 * inset), ph = Math.Max(1, h - 2 * inset);
            photo.Margin = new Thickness(inset);
            photo.Width = pw;
            photo.Height = ph;
            double inner = Math.Max(0, radius - inset);
            photo.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), inner, inner);
            clip.Opacity = chrome;
            // The shadow grows in with the frame. Blurring a screen-sized
            // shadow while the capture is still large would cost the most
            // expensive frames for a shadow nobody sees against the screen.
            shadow.Opacity = 0.26 * chrome;
            var effect = chrome > 0.02 ? shadow : null;
            if (card.Effect != effect) card.Effect = effect;
        }

        void UpdateFall(double raw)
        {
            double e = raw * raw * raw;
            var to = window.ToLocal(toPx);
            Canvas.SetTop(card, to.Y + 520 * e);
            rotate.Angle = tilt + (tilt * 7 + 20) * e;
            card.Opacity = 1 - e;
        }
    }

    /// <summary>
    /// The photo following the pointer while it is dragged out. Windows only
    /// shows a picture under the pointer when the source provides one, so the
    /// line draws its own, and flies it back if the drop is refused.
    /// </summary>
    sealed class DragGhost
    {
        const double Margin = 18;

        readonly OverlayWindow window = new OverlayWindow(clickThrough: true);
        readonly DispatcherTimer timer;
        readonly PxRect start;
        readonly int margin;
        readonly POINT grab;

        public DragGhost(BitmapSource image, PxRect photo, double scale)
        {
            margin = (int)Math.Ceiling(Margin * scale);
            start = photo.Inflate(margin, margin);
            var cursor = Native.Cursor();
            grab = new POINT(cursor.X - start.X, cursor.Y - start.Y);

            double w = photo.Width / scale, h = photo.Height / scale, r = Layout.FrameRadius - Layout.FrameInset;
            var picture = new Image
            {
                Source = image,
                Stretch = Stretch.Fill,
                Width = w,
                Height = h,
                Margin = new Thickness(Margin),
                Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            window.Content = new Grid
            {
                Children = { picture },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 7, Direction = 270, Opacity = 0.32 }
            };
            window.Opacity = 0.92;
            window.SetPixelBounds(start);
            window.Show();
            window.ApplyBounds();

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (s, e) => Follow();
            timer.Start();
        }

        void Follow()
        {
            var c = Native.Cursor();
            window.SetPixelBounds(new PxRect(c.X - grab.X, c.Y - grab.Y, start.Width, start.Height));
        }

        /// <param name="backTo">Where the photo is on the line, when the drop was refused.</param>
        public void End(PxRect? backTo)
        {
            timer.Stop();
            if (backTo == null)
            {
                window.Close();
                return;
            }
            var from = window.PixelBounds;
            var to = backTo.Value.Inflate(margin, margin);
            var flyBack = new Motion(0, k => window.SetPixelBounds(new PxRect(
                (int)Math.Round(Easing.Lerp(from.X, to.X, k)), (int)Math.Round(Easing.Lerp(from.Y, to.Y, k)),
                start.Width, start.Height)), 0.0001);
            flyBack.Tween(1, 0.22, Ease.Out, window.Close);
        }
    }
}

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
    /// The same kind of window draws a discarded card falling, so it is never
    /// cut off by the line's strip.
    /// </summary>
    sealed class Flight
    {
        public const double Duration = 0.5;
        /// <summary>How high the gentle arc rises halfway.</summary>
        const double Arc = 30;

        /// <summary>
        /// A transparent window with a card in it, kept between flights. Making a
        /// new full-screen window for each capture stalled the screen for up to a
        /// second; reusing one costs nothing.
        /// </summary>
        sealed class Surface
        {
            public readonly OverlayWindow Window = new OverlayWindow(clickThrough: true);
            public readonly Canvas Stage = new Canvas();
            public readonly Grid Card = new Grid();
            public readonly RotateTransform Rotate = new RotateTransform();
            public readonly DropShadowEffect Shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Direction = 270 };
            public readonly Border Glass = new Border { Background = Palette.Glass };
            public readonly Image Photo = new Image { Stretch = Stretch.UniformToFill };
            public readonly Border Edge = new Border { BorderBrush = Palette.Edge, BorderThickness = new Thickness(0.75) };
            public readonly FrameworkElement Clip = Visuals.Clothespin();

            public Surface()
            {
                // It is moving; the sharper filter would only cost frames.
                RenderOptions.SetBitmapScalingMode(Photo, BitmapScalingMode.Linear);
                Clip.HorizontalAlignment = HorizontalAlignment.Center;
                Clip.VerticalAlignment = VerticalAlignment.Top;
                // The clip grips the top edge: 26 tall, 12 of them over the card.
                Clip.Margin = new Thickness(0, -(26 - 12), 0, 0);
                Card.Children.Add(Glass);
                Card.Children.Add(Photo);
                Card.Children.Add(Edge);
                Card.Children.Add(Clip);
                Card.RenderTransform = Rotate;
                Stage.Children.Add(Card);
                Window.Content = Stage;
                Window.Opacity = 0;
            }
        }

        static readonly Stack<Surface> spare = new Stack<Surface>();
        static readonly List<Flight> current = new List<Flight>();

        /// <summary>
        /// Makes the first flight's window ahead of time, while nothing is moving,
        /// and has it draw a small card with everything a flight draws, once, out
        /// of sight. The renderer prepares each kind of drawing the first time it
        /// meets it, which took a full second when that was the first capture.
        /// </summary>
        public static void Prewarm()
        {
            if (spare.Count > 0) return;
            var surface = new Surface();
            var pixels = new byte[64 * 40 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 120; pixels[i + 1] = 130; pixels[i + 2] = 140; pixels[i + 3] = 255; }
            var sample = BitmapSource.Create(64, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            sample.Freeze();
            surface.Photo.Source = sample;
            surface.Card.Width = 200;
            surface.Card.Height = 130;
            Canvas.SetLeft(surface.Card, 50);
            Canvas.SetTop(surface.Card, 40);
            surface.Glass.CornerRadius = surface.Edge.CornerRadius = new CornerRadius(Layout.FrameRadius);
            surface.Photo.Margin = new Thickness(Layout.FrameInset);
            surface.Photo.Clip = new RectangleGeometry(new Rect(0, 0, 192, 122), 12, 12);
            surface.Rotate.Angle = 2;
            surface.Card.Effect = surface.Shadow;
            surface.Shadow.Opacity = 0.26;
            var corner = Monitors.Primary?.WorkArea ?? new PxRect(0, 0, 400, 300);
            // One percent opaque, in a corner, for a moment: drawn for real, but not seen.
            surface.Window.Opacity = 0.01;
            surface.Window.SetPixelBounds(new PxRect(corner.Right - 320, corner.Bottom - 240, 300, 220));
            surface.Window.Show();
            Delay.Run(0.4, () =>
            {
                surface.Window.Opacity = 0;
                surface.Photo.Source = null;
                surface.Card.Effect = null;
                surface.Window.SetPixelBounds(new PxRect(corner.X, corner.Y, 2, 2));
                surface.Window.Hide();
                spare.Push(surface);
            });
        }

        readonly Surface s;
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

        /// <summary>A discarded card falling off the line: 520 points down, tilting further, fading, ease in.</summary>
        public static void Fall(BitmapSource image, PxRect card, double tilt, MonitorInfo monitor)
        {
            double s = monitor.Scale;
            var region = PxRect.FromLTRB(card.X - (int)(220 * s), card.Y - (int)(40 * s),
                                         card.Right + (int)(220 * s), card.Bottom + (int)(560 * s)).Intersect(monitor.Bounds);
            new Flight(image, card, card, tilt, region, falling: true).Run(0.45);
        }

        Flight(BitmapSource image, PxRect from, PxRect to, double tilt, PxRect region, bool falling)
        {
            fromPx = from;
            toPx = to;
            this.tilt = tilt;
            this.falling = falling;
            s = spare.Count > 0 ? spare.Pop() : new Surface();
            s.Photo.Source = image;
            s.Card.Opacity = 1;
            s.Window.SetPixelBounds(region);
        }

        void Run(double seconds)
        {
            current.Add(this);
            if (falling) { Update(1); UpdateFall(0); }
            else Update(0);
            // Shown fully transparent, then made visible together with the
            // first new frame, so nothing from the previous flight flashes up.
            s.Window.Opacity = 0;
            s.Window.Show();
            s.Window.ApplyBounds();
            s.Window.Opacity = 1;
            progress = new Motion(0, k => { if (falling) UpdateFall(k); else Update(k); }, 0.0001);
            progress.Tween(1, seconds, Ease.Linear, Finish);
        }

        void Finish()
        {
            if (Animator.Measuring) Log.Info("Flight landed");
            completion();
            if (falling)
            {
                Release();
                return;
            }
            // The real card fades in underneath; this one fades out over it.
            var fade = new Motion(1, v => s.Window.Opacity = v, 0.002);
            fade.Tween(0, 0.14, Ease.Linear, Release);
        }

        void Release()
        {
            if (Animator.Measuring) Log.Info("Flight released");
            current.Remove(this);
            s.Window.Opacity = 0;
            s.Photo.Source = null;
            // Put away small: a hidden screen-sized window still costs the
            // renderer to keep, and stalled the frames that followed each flight.
            var corner = Monitors.Primary?.Bounds ?? new PxRect(0, 0, 1, 1);
            s.Window.SetPixelBounds(new PxRect(corner.X, corner.Y, 2, 2));
            s.Window.Hide();
            if (spare.Count < 3) spare.Push(s);
            else s.Window.Close();
        }

        void Update(double raw)
        {
            // The capture shrinks quickly as it lifts off, then glides to the
            // line: snappier, and the costly screen-sized frames are over at once.
            double k = Easing.Apply(Ease.InOutCubic, raw);
            double shrink = 1 - Math.Pow(1 - raw, 3);
            double chrome = Easing.Smooth(shrink, 0.4, 1);
            var from = s.Window.ToLocal(fromPx);
            var to = s.Window.ToLocal(toPx);

            double w = Easing.Lerp(from.Width, to.Width, shrink), h = Easing.Lerp(from.Height, to.Height, shrink);
            double topX = Easing.Lerp(from.X + from.Width / 2, to.X + to.Width / 2, k);
            double topY = Easing.Lerp(from.Y, to.Y, k) - Math.Sin(Math.PI * k) * Arc;
            double inset = Layout.FrameInset * shrink;
            double radius = Easing.Lerp(0, Layout.FrameRadius, shrink);

            var card = s.Card;
            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, topX - w / 2);
            Canvas.SetTop(card, topY);
            s.Rotate.CenterX = w / 2;
            s.Rotate.CenterY = 0;
            s.Rotate.Angle = tilt * k;

            // Nothing is drawn that cannot be seen yet.
            var chromeVisible = chrome > 0.01 ? Visibility.Visible : Visibility.Hidden;
            s.Glass.Visibility = s.Edge.Visibility = s.Clip.Visibility = chromeVisible;
            s.Glass.CornerRadius = new CornerRadius(radius);
            s.Glass.Opacity = chrome;
            s.Edge.CornerRadius = new CornerRadius(radius);
            s.Edge.Opacity = chrome;
            s.Clip.Opacity = chrome;
            double pw = Math.Max(1, w - 2 * inset), ph = Math.Max(1, h - 2 * inset);
            s.Photo.Margin = new Thickness(inset);
            s.Photo.Width = pw;
            s.Photo.Height = ph;
            double inner = Math.Max(0, radius - inset);
            s.Photo.Clip = inner > 0.5 ? new RectangleGeometry(new Rect(0, 0, pw, ph), inner, inner) : null;
            // The shadow grows in with the frame, never under a screen-sized capture.
            s.Shadow.Opacity = 0.26 * chrome;
            var effect = chrome > 0.02 ? s.Shadow : null;
            if (card.Effect != effect) card.Effect = effect;
        }

        void UpdateFall(double raw)
        {
            double e = raw * raw * raw;
            var to = s.Window.ToLocal(toPx);
            Canvas.SetTop(s.Card, to.Y + 520 * e);
            s.Rotate.Angle = tilt + (tilt * 7 + 20) * e;
            s.Card.Opacity = 1 - e;
        }
    }

    /// <summary>
    /// The photo following the pointer while it is dragged out. Windows only
    /// shows a picture under the pointer when the source provides one, so the
    /// line draws its own, and flies it back if the drop is refused. One window
    /// serves every drag, so a drag starts without a stall.
    /// </summary>
    sealed class DragGhost
    {
        const double Margin = 18;

        static OverlayWindow window;
        static Image picture;
        readonly DispatcherTimer timer;
        readonly PxRect start;
        readonly int margin;
        readonly POINT grab;

        public static void Prewarm()
        {
            if (window != null) return;
            picture = new Image
            {
                Stretch = Stretch.Fill,
                Margin = new Thickness(Margin),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.Linear);
            window = new OverlayWindow(clickThrough: true)
            {
                Content = new Grid
                {
                    Children = { picture },
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 7, Direction = 270, Opacity = 0.32 }
                },
                Opacity = 0
            };
            var corner = Monitors.Primary?.Bounds ?? new PxRect(0, 0, 1, 1);
            window.SetPixelBounds(new PxRect(corner.X, corner.Y, 2, 2));
            window.Show();
            window.Hide();
        }

        public DragGhost(BitmapSource image, PxRect photo, double scale)
        {
            Prewarm();
            margin = (int)Math.Ceiling(Margin * scale);
            start = photo.Inflate(margin, margin);
            var cursor = Native.Cursor();
            grab = new POINT(cursor.X - start.X, cursor.Y - start.Y);

            double w = photo.Width / scale, h = photo.Height / scale, r = Layout.FrameRadius - Layout.FrameInset;
            picture.Source = image;
            picture.Width = w;
            picture.Height = h;
            picture.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            window.Opacity = 0;
            window.SetPixelBounds(start);
            window.Show();
            window.ApplyBounds();
            window.Opacity = 0.92;

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
                Put();
                return;
            }
            var from = window.PixelBounds;
            var to = backTo.Value.Inflate(margin, margin);
            var flyBack = new Motion(0, k => window.SetPixelBounds(new PxRect(
                (int)Math.Round(Easing.Lerp(from.X, to.X, k)), (int)Math.Round(Easing.Lerp(from.Y, to.Y, k)),
                start.Width, start.Height)), 0.0001);
            flyBack.Tween(1, 0.2, Ease.Out, Put);
        }

        static void Put()
        {
            window.Opacity = 0;
            window.Hide();
            picture.Source = null;
        }
    }
}

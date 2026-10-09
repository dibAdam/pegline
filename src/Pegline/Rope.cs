using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pegline
{
    struct RopeLoad
    {
        public double X, Weight;
        public RopeLoad(double x, double weight) { X = x; Weight = weight; }
    }

    /// <summary>
    /// The line as a real string under tension. Its own weight gives it the
    /// gentle sag; every photo pulls it down a little more where it hangs.
    /// Take one off and the line springs up and bounces its neighbours;
    /// a gust sends a ripple along it.
    /// It is a small wave equation, stepped only while something moves.
    /// </summary>
    sealed class RopeSim : IAnimated
    {
        /// <summary>Distance between the points of the simulated line.</summary>
        const double Spacing = 16;
        /// <summary>How fast a ripple runs along the line. A wave crosses a 1080p display in half a second.</summary>
        const double WaveSpeed = 4000;
        /// <summary>How quickly movement dies down: a bounce settles in a little over a second.</summary>
        const double Damping = 4.2;
        /// <summary>The line starts beyond the screen edges, so it seems to come from further away.</summary>
        const double Overhang = 20;
        /// <summary>How far a photo of average weight pulls the middle of the line down.</summary>
        const double CardSag = 3.2;

        /// <summary>Movement smaller than this is not worth a redraw.</summary>
        const double Visible = 0.08;

        readonly Func<IEnumerable<RopeLoad>> loads;
        double[] y = new double[2], v = new double[2], force = new double[2], drawn = new double[2];
        double left, dx, span, selfWeight, cardForce, loadKey;
        bool awake;

        /// <summary>Raised after every step, so the photos and lights can follow the line.</summary>
        public event Action Moved;

        public double Width { get; private set; }
        public int Count => y.Length;

        public RopeSim(Func<IEnumerable<RopeLoad>> loads)
        {
            this.loads = loads;
        }

        public void Resize(double width)
        {
            Width = width;
            span = width + 2 * Overhang;
            int n = Math.Max(8, (int)Math.Ceiling(span / Spacing)) + 1;
            left = -Overhang;
            dx = span / (n - 1);
            y = new double[n];
            v = new double[n];
            force = new double[n];
            drawn = new double[n];
            double c2 = WaveSpeed * WaveSpeed;
            // Uniform weight hangs a string as a parabola, deepest by g L² / 8c²:
            // the same sag the line always had.
            double sag = Math.Min(24, width * 0.0125);
            selfWeight = sag * 8 * c2 / (span * span);
            // A weight P at the middle pulls it down by P L / 4c².
            cardForce = CardSag * 4 * c2 / span;
            Settle();
            Announce();
        }

        /// <summary>Tells the photos, lights and drawing that the line moved, and remembers where it was drawn.</summary>
        void Announce()
        {
            Array.Copy(y, drawn, y.Length);
            Moved?.Invoke();
        }

        public double YAt(double x)
        {
            double f = (x - left) / dx;
            int i = (int)Math.Floor(f);
            if (i < 0) return y[0];
            if (i >= y.Length - 1) return y[y.Length - 1];
            return y[i] + (y[i + 1] - y[i]) * (f - i);
        }

        /// <summary>The angle of the line at x, in degrees, positive going down to the right.</summary>
        public double SlopeAt(double x)
        {
            int i = (int)Math.Floor((x - left) / dx);
            i = Math.Max(0, Math.Min(y.Length - 2, i));
            return Math.Atan((y[i + 1] - y[i]) / dx) * 180 / Math.PI;
        }

        public Point PointAt(int i) => new Point(left + i * dx, y[i]);

        public void Wake()
        {
            if (awake) return;
            awake = true;
            Animator.Add(this);
        }

        /// <summary>Gives the line a push around x, in points per second, downwards when positive.</summary>
        public void Pluck(double x, double velocity, double reach = 40)
        {
            for (int i = 1; i < y.Length - 1; i++)
            {
                double d = (left + i * dx - x) / reach;
                if (Math.Abs(d) < 3) v[i] += velocity * Math.Exp(-d * d / 2);
            }
            Wake();
        }

        /// <summary>Puts the line straight into its resting shape, without a bounce.</summary>
        public void Settle()
        {
            UpdateForces();
            int n = y.Length;
            double c2 = WaveSpeed * WaveSpeed, top = Layout.RopeTop;
            // Rest: u[i-1] - 2u[i] + u[i+1] = -force[i] dx² / c², with both ends pinned.
            // A tridiagonal system, solved in one sweep each way.
            var cp = new double[n];
            var dp = new double[n];
            var u = new double[n];
            for (int i = 1; i < n - 1; i++)
            {
                double d = -force[i] * dx * dx / c2;
                double a = i > 1 ? 1 : 0, c = i < n - 2 ? 1 : 0;
                double m = -2 - a * cp[i - 1];
                cp[i] = c / m;
                dp[i] = (d - a * dp[i - 1]) / m;
            }
            for (int i = n - 2; i >= 1; i--) u[i] = dp[i] - cp[i] * u[i + 1];
            for (int i = 0; i < n; i++)
            {
                y[i] = top + u[i];
                v[i] = 0;
            }
        }

        bool UpdateForces()
        {
            int n = y.Length;
            for (int i = 0; i < n; i++) force[i] = selfWeight;
            double key = 0;
            if (loads != null)
            {
                foreach (var load in loads())
                {
                    double f = (load.X - left) / dx;
                    int i = (int)Math.Floor(f);
                    if (i < 1 || i > n - 3) continue;
                    double t = f - i, p = cardForce * load.Weight / dx;
                    force[i] += p * (1 - t);
                    force[i + 1] += p * t;
                    key += (load.X + 3.1) * (load.Weight + 1.7);
                }
            }
            bool changed = Math.Abs(key - loadKey) > 1e-6;
            loadKey = key;
            return changed;
        }

        public bool Step(double dt)
        {
            if (!awake) return false;
            bool changed = UpdateForces();
            int n = y.Length;
            double k = WaveSpeed * WaveSpeed / (dx * dx);
            int steps = Math.Max(1, (int)Math.Ceiling(dt / (0.5 * dx / WaveSpeed)));
            double h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                for (int i = 1; i < n - 1; i++)
                    v[i] += (k * (y[i - 1] - 2 * y[i] + y[i + 1]) + force[i] - Damping * v[i]) * h;
                for (int i = 1; i < n - 1; i++)
                    y[i] += v[i] * h;
            }

            // At rest when nothing moves and nothing pulls: speed alone is not
            // enough, since a bouncing line stands still at the top of each bounce.
            double fastest = 0, pull = 0, shift = 0;
            for (int i = 1; i < n - 1; i++)
            {
                fastest = Math.Max(fastest, Math.Abs(v[i]));
                pull = Math.Max(pull, Math.Abs(k * (y[i - 1] - 2 * y[i] + y[i + 1]) + force[i]));
                shift = Math.Max(shift, Math.Abs(y[i] - drawn[i]));
            }
            // Once what is left of a bounce is too small to see, the line is put
            // straight into its resting shape instead of being stepped through it.
            bool resting = !changed && fastest < 3 && pull < 30;
            if (resting)
            {
                Settle();
                Announce();
                awake = false;
                return false;
            }
            // The last part of a bounce is mostly sub-pixel: drawing it would cost frames nobody sees.
            if (shift > Visible) Announce();
            return awake;
        }
    }

    /// <summary>
    /// A thin, neutral line: a mid gray core with a faint highlight and a soft
    /// shadow, so it reads on light and dark backgrounds alike. It fades out at
    /// both ends so it seems to come from beyond the screen.
    /// </summary>
    sealed class RopeView : Grid
    {
        readonly Path haze, shadow, core, highlight;

        public RopeView()
        {
            IsHitTestVisible = false;
            // A soft shadow from two wide, faint strokes: a blur would have to
            // be recomputed along the whole line on every frame of a bounce.
            haze = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)),
                StrokeThickness = 3.6,
                RenderTransform = new TranslateTransform(0, 1.6)
            };
            shadow = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                StrokeThickness = 1.8,
                RenderTransform = new TranslateTransform(0, 1.2)
            };
            core = new Path { Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140)), StrokeThickness = 1.2 };
            highlight = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)),
                StrokeThickness = 0.4,
                RenderTransform = new TranslateTransform(0, -0.35)
            };
            Children.Add(haze);
            Children.Add(shadow);
            Children.Add(core);
            Children.Add(highlight);
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Black, 0.08),
                    new GradientStop(Colors.Black, 0.92),
                    new GradientStop(Colors.Transparent, 1),
                }
            };
        }

        /// <summary>Redraws the line through the simulated points, smoothed with curves between their midpoints.</summary>
        public void Update(RopeSim rope)
        {
            Width = rope.Width;
            Height = Layout.PanelHeight;
            int n = rope.Count;
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                var first = rope.PointAt(0);
                var second = rope.PointAt(1);
                c.BeginFigure(first, false, false);
                c.LineTo(Mid(first, second), true, true);
                var points = new List<Point>(2 * n);
                for (int i = 1; i < n - 1; i++)
                {
                    var p = rope.PointAt(i);
                    points.Add(p);
                    points.Add(Mid(p, rope.PointAt(i + 1)));
                }
                c.PolyQuadraticBezierTo(points, true, true);
                c.LineTo(rope.PointAt(n - 1), true, true);
            }
            geometry.Freeze();
            haze.Data = shadow.Data = core.Data = highlight.Data = geometry;
        }

        static Point Mid(Point a, Point b) => new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    /// <summary>
    /// After sunset the line becomes a string of warm fairy lights. They
    /// hang from the line, so they bounce with it, and twinkle gently while
    /// the line is down.
    /// </summary>
    sealed class LightsView : Canvas
    {
        const double Spacing = 56;

        sealed class Bulb
        {
            public double X, Phase, Speed;
            public Image Body, Glow;
        }

        const double GlowSize = 30, BulbWidth = 6, BulbHeight = 10.4;

        static readonly Brush BulbBrush = Frozen(new RadialGradientBrush(Color.FromRgb(255, 251, 236), Color.FromRgb(255, 186, 84))
        {
            GradientOrigin = new Point(0.42, 0.32)
        });
        static readonly Brush GlowBrush = Frozen(new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(170, 255, 205, 120), 0),
                new GradientStop(Color.FromArgb(70, 255, 178, 80), 0.42),
                new GradientStop(Color.FromArgb(0, 255, 170, 70), 1),
            }
        });
        static readonly Brush CapBrush = Frozen(new SolidColorBrush(Color.FromRgb(70, 76, 66)));

        readonly List<Bulb> bulbs = new List<Bulb>();
        readonly DispatcherTimer twinkle;
        readonly Motion fade;
        readonly Random random = new Random();
        double time;
        bool on, revealed;

        public LightsView()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            fade = new Motion(0, v =>
            {
                Opacity = v;
                Visibility = v > 0.001 ? Visibility.Visible : Visibility.Collapsed;
            }, 0.002);
            twinkle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            twinkle.Tick += (s, e) => Twinkle();
        }

        public bool IsOn => on;

        /// <summary>The bulbs are drawn once into bitmaps; twinkling only changes their opacity.</summary>
        public void Build(double width, double scale)
        {
            Children.Clear();
            bulbs.Clear();
            var glow = Bake(new Ellipse { Width = GlowSize, Height = GlowSize, Fill = GlowBrush }, new Size(GlowSize, GlowSize), scale);
            var bulbCanvas = new Canvas { Width = BulbWidth, Height = BulbHeight };
            var cap = new Rectangle { Width = 3.2, Height = 3, RadiusX = 0.8, RadiusY = 0.8, Fill = CapBrush };
            Canvas.SetLeft(cap, 1.4);
            var glass = new Ellipse { Width = 5.6, Height = 7.6, Fill = BulbBrush };
            Canvas.SetLeft(glass, 0.2);
            Canvas.SetTop(glass, 2.4);
            bulbCanvas.Children.Add(cap);
            bulbCanvas.Children.Add(glass);
            var body = Bake(bulbCanvas, new Size(BulbWidth, BulbHeight), scale);

            int count = Math.Max(2, (int)((width - 40) / Spacing));
            double step = (width - 40) / count;
            for (int i = 0; i < count; i++)
            {
                var bulb = new Bulb
                {
                    X = 20 + step * (i + 0.5),
                    Phase = random.NextDouble() * Math.PI * 2,
                    Speed = 1.4 + random.NextDouble() * 2.2,
                    Glow = new Image { Source = glow, Width = GlowSize, Height = GlowSize },
                    Body = new Image { Source = body, Width = BulbWidth, Height = BulbHeight }
                };
                Children.Add(bulb.Glow);
                Children.Add(bulb.Body);
                bulbs.Add(bulb);
            }
        }

        static BitmapSource Bake(FrameworkElement element, Size size, double scale)
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
                                                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(element);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Each bulb hangs just under the line at its own point.</summary>
        public void Follow(RopeSim rope)
        {
            if (!on && Visibility != Visibility.Visible) return;
            foreach (var b in bulbs)
            {
                double top = rope.YAt(b.X) + 0.6;
                SetLeft(b.Body, b.X - BulbWidth / 2);
                SetTop(b.Body, top);
                SetLeft(b.Glow, b.X - GlowSize / 2);
                SetTop(b.Glow, top + 6.2 - GlowSize / 2);
            }
        }

        public void SetOn(bool value, RopeSim rope)
        {
            if (value == on) return;
            on = value;
            if (on) Follow(rope);
            fade.Tween(on ? 1 : 0, 1.4, Ease.InOut);
            UpdateTwinkle();
        }

        /// <summary>Twinkling only while someone can see it.</summary>
        public void SetRevealed(bool value)
        {
            revealed = value;
            UpdateTwinkle();
        }

        void UpdateTwinkle()
        {
            if (on && revealed) twinkle.Start();
            else twinkle.Stop();
        }

        void Twinkle()
        {
            time += twinkle.Interval.TotalSeconds;
            foreach (var b in bulbs)
            {
                double glow = 0.5 + 0.5 * Math.Sin(b.Phase + time * b.Speed);
                // Now and then a bulb flickers, like an old string of lights.
                if (random.NextDouble() < 0.004) glow = 0.1;
                b.Glow.Opacity = 0.45 + 0.55 * glow;
                b.Body.Opacity = 0.82 + 0.18 * glow;
            }
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }

    /// <summary>
    /// Roughly when the sun is down, from the clock alone: no location and no
    /// network. Sunset moves with the seasons, flipped in the southern hemisphere.
    /// </summary>
    static class Night
    {
        static readonly HashSet<string> Southern = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AR", "AU", "BO", "BR", "BW", "CL", "FJ", "LS", "MG", "MU", "MW", "MZ", "NA", "NZ",
            "PE", "PY", "SZ", "UY", "ZA", "ZM", "ZW"
        };

        static readonly bool south = IsSouthern();

        static bool IsSouthern()
        {
            try { return Southern.Contains(RegionInfo.CurrentRegion.TwoLetterISORegionName); }
            catch { return false; }
        }

        public static bool IsDark(DateTime local)
        {
            // +1 at the June solstice, -1 at the December one.
            double season = Math.Cos(2 * Math.PI * (local.DayOfYear - 172) / 365.25);
            if (south) season = -season;
            double sunset = 18.2 + 1.8 * season;
            double sunrise = 6.3 - 1.3 * season;
            if (TimeZoneInfo.Local.IsDaylightSavingTime(local))
            {
                sunset += 1;
                sunrise += 1;
            }
            double hour = local.TimeOfDay.TotalHours;
            return hour >= sunset + 0.3 || hour < sunrise - 0.3;
        }
    }
}

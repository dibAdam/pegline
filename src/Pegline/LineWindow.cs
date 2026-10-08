using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>
    /// The transparent strip along the top of a display: the rope and the
    /// photos hanging from it. It floats over every app, never takes focus,
    /// and lets clicks through everywhere except over the photos.
    /// Tucked away, the whole line waits above the top edge and slides down
    /// when called, the way an auto-hiding taskbar does.
    /// </summary>
    sealed class LineWindow : OverlayWindow
    {
        static readonly Brush DropBrush = Frozen(new RadialGradientBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xB0, 0x8E, 0xC9, 0xEE))
        {
            GradientOrigin = new Point(0.35, 0.3)
        });
        static readonly Brush DropEdge = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x5A, 0x8C, 0xB0)));

        readonly Line line;
        readonly Canvas stage = new Canvas();
        readonly TranslateTransform shift = new TranslateTransform();
        readonly RopeView ropeView = new RopeView();
        readonly LightsView lights = new LightsView();
        readonly Canvas drops = new Canvas { IsHitTestVisible = false };
        readonly Border hint;
        readonly Motion reveal, hintFade;
        readonly Dictionary<Guid, CardView> cards = new Dictionary<Guid, CardView>();
        readonly DispatcherTimer weather;
        readonly Random random = new Random();
        Point? lastPointer;
        DateTime lastPointerAt;

        static double Hidden => -(Layout.PanelHeight + 12);

        public RopeSim Rope { get; }
        public MonitorInfo Monitor { get; private set; }
        public double StageWidth { get; private set; } = 1200;

        /// <summary>A photo asks to come off the line and be pinned to the screen: the photo, where it is on screen, and whether it follows the pointer.</summary>
        public event Action<Pegged, PxRect, bool> PinRequested;

        public LineWindow(Line line) : base(clickThrough: true)
        {
            this.line = line;
            Rope = new RopeSim(Loads);
            Rope.Moved += OnRopeMoved;

            stage.RenderTransform = shift;
            Content = stage;
            stage.Children.Add(ropeView);
            stage.Children.Add(lights);
            hint = MakeHint();
            stage.Children.Add(hint);
            Panel.SetZIndex(drops, 10000);
            stage.Children.Add(drops);

            reveal = new Motion(Hidden, v => shift.Y = v, 0.05);
            hintFade = new Motion(0, v => hint.Opacity = v, 0.002);

            SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width <= 0 || Math.Abs(e.NewSize.Width - StageWidth) < 0.5) return;
                StageWidth = e.NewSize.Width;
                Relayout();
            };
            line.ItemsChanged += Sync;
            line.ItemUpdated += item =>
            {
                if (cards.TryGetValue(item.Id, out var card)) card.Update();
                // It landed: now it weighs on the line.
                Rope.Wake();
            };
            line.Gust += Gust;
            line.StateChanged += () => { foreach (var card in cards.Values) card.RefreshState(); };
            line.RevealedChanged += OnRevealedChanged;

            // The cards are baked bitmaps: a new theme or display scale means baking them again.
            Theme.Changed += () => { foreach (var card in cards.Values) card.Bake(); };

            weather = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            weather.Tick += (s, e) => Weather();
            weather.Start();

            Relayout();
            Sync();
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        Border MakeHint() => new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13, 6, 13, 7),
            Background = Palette.Capsule,
            BorderBrush = Palette.Edge,
            BorderThickness = new Thickness(0.75),
            IsHitTestVisible = false,
            Opacity = 0,
            Child = new TextBlock
            {
                Text = L("Take a screenshot with Win + Shift + S and it will hang here",
                         "Haz una captura con Win + Mayús + S y se quedará colgada aquí",
                         "Faites une capture avec Win + Maj + S, elle viendra s’accrocher ici"),
                FontFamily = Visuals.UiFont,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = Palette.Secondary
            }
        };

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lights.Build(StageWidth, Scale);
                lights.Follow(Rope);
                foreach (var card in cards.Values) card.CheckScale();
            }));
        }

        /// <summary>The line hangs at the top of the display you are using, below a taskbar placed at the top.</summary>
        public void PlaceOnScreen(MonitorInfo monitor = null)
        {
            monitor = monitor ?? Monitors.UnderCursor() ?? Monitors.Primary;
            if (monitor == null) return;
            Monitor = monitor;
            var work = monitor.WorkArea;
            var target = new PxRect(work.X, work.Y, work.Width, (int)Math.Ceiling(Layout.PanelHeight * monitor.Scale));
            if (target.Equals(PixelBounds)) return;
            StageWidth = work.Width / monitor.Scale;
            SetPixelBounds(target);
            Relayout();
        }

        void Relayout()
        {
            double w = StageWidth;
            var items = line.Items;
            for (int i = 0; i < items.Count; i++)
                if (cards.TryGetValue(items[i].Id, out var card)) card.X.Set(Layout.X(i, items.Count, w));
            lights.Build(w, Scale);
            foreach (var card in cards.Values) card.CheckScale();
            Rope.Resize(w);
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hint, (w - hint.DesiredSize.Width) / 2);
            OnRopeMoved();
        }

        IEnumerable<RopeLoad> Loads()
        {
            foreach (var card in cards.Values)
                if (card.CarriesWeight) yield return new RopeLoad(card.CurrentX, card.Weight);
        }

        void OnRopeMoved()
        {
            ropeView.Update(Rope);
            foreach (var card in cards.Values) card.FollowRope();
            lights.Follow(Rope);
            double w = StageWidth;
            Canvas.SetTop(hint, Rope.YAt(w / 2) + 34 - hint.DesiredSize.Height / 2);
        }

        void Sync()
        {
            var items = line.Items;
            var ids = new HashSet<Guid>(items.Select(i => i.Id));
            foreach (var gone in cards.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                stage.Children.Remove(cards[gone]);
                cards.Remove(gone);
            }

            double w = StageWidth;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                double x = Layout.X(i, items.Count, w);
                if (!cards.TryGetValue(item.Id, out var card))
                {
                    card = new CardView(item, line, this);
                    cards[item.Id] = card;
                    stage.Children.Add(card);
                    card.Appear(x);
                }
                else if (Math.Abs(card.X.Target - x) > 0.01)
                {
                    card.MoveTo(x);
                }
                card.Update();
            }
            // Something was hung, taken down or let fall: the line feels it.
            Rope.Wake();

            double hintTarget = items.Count == 0 ? 1 : 0;
            if (hintFade.Target != hintTarget) hintFade.Tween(hintTarget, 0.3, Ease.InOut);
        }

        void OnRevealedChanged()
        {
            if (line.Revealed)
            {
                reveal.Spring(0, 0.42, 0.82);
            }
            else
            {
                reveal.Tween(Hidden, 0.22, Ease.In);
                ClickThrough = true;
                lastPointer = null;
            }
            lights.SetRevealed(line.Revealed);
        }

        // MARK: Wind and weather

        /// <summary>
        /// A gust travels along the line from one side, reaching each photo a
        /// moment after the one before, and lifts the line a little as it passes.
        /// </summary>
        void Gust()
        {
            var live = cards.Values.Where(c => !c.Item.Falling).ToList();
            if (live.Count == 0) return;
            int from = random.Next(2) == 0 ? -1 : 1;
            double strength = 1.6 + random.NextDouble() * 1.8, w = StageWidth;
            foreach (var card in live)
            {
                double f = card.CurrentX / w;
                double delay = (from < 0 ? f : 1 - f) * 0.7 + random.NextDouble() * 0.08;
                // Wind from the left pushes the bottoms to the right, which is a negative angle.
                card.Breeze(delay, from * strength * (0.8 + random.NextDouble() * 0.4));
            }
            Rope.Pluck(from < 0 ? w * 0.12 : w * 0.88, -90, 120);
        }

        /// <summary>
        /// Called with the pointer 30 times a second while the line is down.
        /// A quick sweep past the photos sets them swinging. The line itself
        /// ignores the pointer: it only answers to weight and wind.
        /// </summary>
        public void PointerMoved(POINT screen)
        {
            var now = DateTime.UtcNow;
            var p = ToLocal(screen);
            p.Y -= shift.Y;
            var previous = lastPointer;
            double dt = (now - lastPointerAt).TotalSeconds;
            lastPointer = p;
            lastPointerAt = now;
            if (previous == null || dt <= 0 || dt > 0.2 || !line.Revealed || CardView.IsDragging) return;
            Sweep(previous.Value, p, dt);
        }

        void Sweep(Point a, Point b, double dt)
        {
            double w = StageWidth;
            if (Math.Max(a.X, b.X) < 0 || Math.Min(a.X, b.X) > w) return;
            double vx = (b.X - a.X) / dt;
            if (Math.Abs(vx) > 650)
            {
                foreach (var card in cards.Values)
                {
                    if (!card.IsTouchable) continue;
                    var r = card.CardRect;
                    double distance = Math.Abs(card.CurrentX - b.X);
                    if (distance > 110 || b.Y < r.Top - 30 || b.Y > r.Bottom + 24) continue;
                    card.Blow(-vx * 0.012 * (1 - distance / 110));
                }
            }
        }

        /// <summary>Once a second: photos dry, and the wet ones drip while someone can see them.</summary>
        void Weather()
        {
            var now = DateTime.UtcNow;
            foreach (var card in cards.Values.ToList())
            {
                card.Age(now);
                if (line.Revealed && card.IsTouchable && !CardView.IsDragging && card.Wetness > 0.3
                    && random.NextDouble() < 0.45 * card.Wetness)
                    Drip(card.DripPoint(random));
            }
        }

        void Drip(Point at)
        {
            var drop = new Ellipse { Width = 3.2, Height = 4.4, Fill = DropBrush, Stroke = DropEdge, StrokeThickness = 0.4 };
            Canvas.SetLeft(drop, at.X - 1.6);
            Canvas.SetTop(drop, at.Y);
            drop.Opacity = 0;
            drops.Children.Add(drop);
            double fall = 34 + random.NextDouble() * 22;
            var motion = new Motion(0, k =>
            {
                Canvas.SetTop(drop, at.Y + fall * k * k);
                drop.Opacity = k < 0.12 ? k / 0.12 : 1 - Math.Max(0, (k - 0.55) / 0.45);
            }, 0.0001);
            motion.Tween(1, 0.65, Ease.Linear, () => drops.Children.Remove(drop));
        }

        /// <summary>Fairy lights on the line, for after sunset.</summary>
        public void SetLights(bool on) => lights.SetOn(on, Rope);

        /// <summary>A little show, for recording the README and for testing: plucks and gusts.</summary>
        public void Demo(int step)
        {
            double w = StageWidth;
            switch (step % 3)
            {
                case 0:
                    Rope.Pluck(w * (0.25 + random.NextDouble() * 0.5), 800);
                    break;
                case 1:
                    Gust();
                    break;
                default:
                    Rope.Pluck(w * (0.2 + random.NextDouble() * 0.6), -600);
                    break;
            }
        }

        // MARK: Pins

        public void RequestPin(CardView card, bool follow) => PinRequested?.Invoke(card.Item, card.PhotoOnScreen(), follow);

        /// <summary>Development: pins the first photo the way the menu does.</summary>
        public void PreviewPin()
        {
            var card = cards.Values.FirstOrDefault(c => c.IsTouchable);
            if (card != null && line.Revealed) RequestPin(card, follow: false);
        }

        // MARK: Geometry

        /// <summary>The photo under a screen point, if any.</summary>
        public Guid? HitTest(POINT p)
        {
            if (!line.Revealed || !IsVisible) return null;
            var local = ToLocal(p);
            local.Y -= shift.Y;
            for (int i = stage.Children.Count - 1; i >= 0; i--)
            {
                if (!(stage.Children[i] is CardView card) || !card.IsTouchable) continue;
                var r = card.CardRect;
                r.Inflate(4, 4);
                if (r.Contains(local)) return card.Item.Id;
            }
            return null;
        }

        /// <summary>Where a card will hang once the line is down, in screen pixels.</summary>
        public PxRect? CardFrame(Guid id)
        {
            var items = line.Items;
            int index = items.FindIndex(i => i.Id == id);
            if (index < 0) return null;
            double w = StageWidth;
            double x = Layout.X(index, items.Count, w);
            double top = Rope.YAt(x) - Layout.PinAbove + Layout.CardOffsetBelowTop;
            var thumb = items[index].Thumb;
            var size = Layout.CardSize(thumb.PixelWidth, thumb.PixelHeight);
            return ToScreen(new Rect(x - size.Width / 2, top, size.Width, size.Height));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>
    /// The transparent strip along the top of a display: the rope and the
    /// photos hanging from it. It floats over every app, never takes focus
    /// unless you open it from the keyboard, and lets clicks through
    /// everywhere except over the photos and its few controls.
    /// Tucked away, the whole line waits above the top edge and slides down
    /// when called, the way an auto-hiding taskbar does; a small tab shows
    /// that something is waiting.
    /// </summary>
    sealed class LineWindow : OverlayWindow
    {
        static readonly Brush DropBrush = Frozen(new RadialGradientBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xB0, 0x8E, 0xC9, 0xEE))
        {
            GradientOrigin = new Point(0.35, 0.3)
        });
        static readonly Brush DropEdge = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x5A, 0x8C, 0xB0)));

        /// <summary>How long the pointer rests on a photo before the big preview opens.</summary>
        const double PreviewDelay = 0.35;

        readonly Line line;
        readonly Canvas stage = new Canvas();
        readonly TranslateTransform shift = new TranslateTransform();
        readonly RopeView ropeView = new RopeView();
        readonly LightsView lights = new LightsView();
        readonly Canvas drops = new Canvas { IsHitTestVisible = false };
        readonly Border hint, older, newer;
        readonly TextBlock olderText, newerText;
        readonly Motion reveal, hintFade;
        /// <summary>Where the window sits when the line is down.</summary>
        PxRect home;
        /// <summary>
        /// Whether the line comes down by moving its window, which needs no
        /// drawing at all, rather than by moving what is inside it. Not when
        /// another display sits above, where the window would show, or under a
        /// taskbar at the top, which it would slide over.
        /// </summary>
        bool slides;
        readonly Dictionary<Guid, CardView> cards = new Dictionary<Guid, CardView>();
        readonly DispatcherTimer weather, hoverTimer, unhoverTimer;
        readonly PreviewWindow preview = new PreviewWindow();
        readonly Random random = new Random();
        Point? lastPointer;
        DateTime lastPointerAt;
        Guid? hoverTarget;
        bool keyboard;
        IntPtr previousForeground;

        static double Hidden => -(Layout.PanelHeight + 12);

        public RopeSim Rope { get; }
        public MonitorInfo Monitor { get; private set; }
        public double StageWidth { get; private set; } = 1200;

        /// <summary>A photo asks to come off the line and be pinned to the screen: the photo, where it is on screen, and whether it follows the pointer.</summary>
        public event Action<Pegged, PxRect, bool> PinRequested;
        /// <summary>Escape, from the keyboard.</summary>
        public event Action CloseRequested;

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

            older = Pill(out olderText, () => line.ScrollOlder());
            newer = Pill(out newerText, () => line.BackToNewest());
            Panel.SetZIndex(older, 9000);
            Panel.SetZIndex(newer, 9000);
            stage.Children.Add(older);
            stage.Children.Add(newer);

            reveal = new Motion(Hidden, ApplyReveal, 0.3);
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
            line.StateChanged += () =>
            {
                foreach (var card in cards.Values) card.RefreshState();
                UpdatePreview();
            };
            line.RevealedChanged += OnRevealedChanged;

            // The cards are baked bitmaps: a new theme or display scale means baking them again.
            Theme.Changed += () => { foreach (var card in cards.Values) card.Bake(); };

            weather = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            weather.Tick += (s, e) => Weather();
            weather.Start();
            hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PreviewDelay) };
            hoverTimer.Tick += (s, e) =>
            {
                hoverTimer.Stop();
                ShowPreview(hoverTarget);
            };
            unhoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.2) };
            unhoverTimer.Tick += (s, e) =>
            {
                unhoverTimer.Stop();
                if (hoverTarget == null) preview.HideNow();
            };

            PreviewMouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Deactivated += (s, e) => ExitKeyboard(restoreFocus: false);

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

        /// <summary>A small clickable label at an end of the line, for looking back through older captures.</summary>
        Border Pill(out TextBlock text, Action click)
        {
            text = new TextBlock
            {
                FontFamily = Visuals.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.Primary
            };
            var p = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 5),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Hand,
                Child = text
            };
            p.MouseLeftButtonUp += (s, e) => click();
            return p;
        }

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
            if (target.Equals(home) && PixelBounds.Width == target.Width) return;
            home = target;
            var above = new PxRect(target.X, target.Y - target.Height - 24, target.Width, target.Height + 24);
            slides = work.Y == monitor.Bounds.Y
                     && !Monitors.All.Any(m => !m.SameAs(monitor) && m.Bounds.Intersects(above))
                     && Environment.GetEnvironmentVariable("PEGLINE_NOSLIDE") != "1";
            StageWidth = work.Width / monitor.Scale;
            ApplyReveal(reveal.Value);
            Relayout();
        }

        /// <summary>Puts the line at its point between tucked away (Hidden) and down (0).</summary>
        void ApplyReveal(double offset)
        {
            if (home.IsEmpty) return;
            if (slides)
            {
                shift.Y = 0;
                int y = home.Y + (int)Math.Round(offset * (Monitor?.Scale ?? 1));
                if (PixelBounds.Width != home.Width || PixelBounds.Height != home.Height || PixelBounds.X != home.X)
                    SetPixelBounds(new PxRect(home.X, y, home.Width, home.Height));
                else if (PixelBounds.Y != y)
                    MoveTo(home.X, y);
            }
            else
            {
                shift.Y = offset;
                if (!PixelBounds.Equals(home)) SetPixelBounds(home);
            }
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
            PlacePills();
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
            UpdatePills();
            if (keyboard && line.SelectedId != null && line.Find(line.SelectedId.Value) == null)
                line.SelectedId = line.Live.LastOrDefault()?.Id;
        }

        void OnRevealedChanged()
        {
            if (line.Revealed)
            {
                reveal.Spring(0, 0.32, 0.86);
            }
            else
            {
                reveal.Tween(Hidden, 0.16, Ease.In);
                ClickThrough = true;
                lastPointer = null;
                preview.HideNow();
                if (keyboard) ExitKeyboard(restoreFocus: true);
            }
            lights.SetRevealed(line.Revealed);
            UpdatePills();
        }

        // MARK: Looking back

        void UpdatePills()
        {
            bool show = line.Revealed;
            int olderCount = line.OlderCount, newerCount = line.NewerCount;
            olderText.Text = "‹  " + string.Format(L("{0} older", "{0} anteriores", "{0} plus anciennes"), olderCount);
            newerText.Text = string.Format(L("Back to newest ({0})", "Volver a las recientes ({0})", "Revenir aux récentes ({0})"), newerCount) + "  ›";
            older.Visibility = show && olderCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            newer.Visibility = show && newerCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlacePills();
        }

        void PlacePills()
        {
            double w = StageWidth;
            older.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            newer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(older, 24);
            Canvas.SetTop(older, Rope.YAt(60) + 12);
            Canvas.SetLeft(newer, w - 24 - newer.DesiredSize.Width);
            Canvas.SetTop(newer, Rope.YAt(w - 60) + 12);
        }

        /// <summary>Whether the pointer is over one of the line's own controls, which need the click.</summary>
        public bool OverControl(POINT p)
        {
            if (!line.Revealed) return false;
            var local = ToLocal(p);
            local.Y -= shift.Y;
            foreach (var pill in new[] { older, newer })
            {
                if (pill.Visibility != Visibility.Visible) continue;
                var r = new Rect(Canvas.GetLeft(pill), Canvas.GetTop(pill), pill.DesiredSize.Width, pill.DesiredSize.Height);
                r.Inflate(3, 3);
                if (r.Contains(local)) return true;
            }
            return false;
        }

        /// <summary>Scrolling over the photos walks back through older captures, and forward again.</summary>
        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (e.Delta < 0) line.ScrollOlder();
            else line.ScrollNewer();
        }

        // MARK: The big preview

        Guid? PreviewTarget()
        {
            if (!line.Revealed || CardView.IsDragging || line.PressedId != null) return null;
            if (keyboard) return line.SelectedId;
            return Settings.GetBool("PreviewOff") ? null : line.HoveredId;
        }

        void UpdatePreview()
        {
            var target = PreviewTarget();
            if (target == hoverTarget) return;
            hoverTarget = target;
            hoverTimer.Stop();
            if (target == null)
            {
                // A short grace, so moving from one photo to the next does not flicker.
                unhoverTimer.Stop();
                unhoverTimer.Start();
                return;
            }
            unhoverTimer.Stop();
            // The large image is decoded in the background while the pointer rests.
            if (cards.TryGetValue(target.Value, out var hovered)) preview.Prefetch(hovered.Item.Path);
            if (keyboard || preview.ShownId != null) ShowPreview(target);
            else hoverTimer.Start();
        }

        void ShowPreview(Guid? id)
        {
            if (id == null || id != PreviewTarget() || Monitor == null) return;
            if (!cards.TryGetValue(id.Value, out var card) || !card.IsTouchable) return;
            try
            {
                preview.ShowFor(card.Item, card.PhotoOnScreen(), Monitors.Find(Monitor.Device) ?? Monitor, keyboard);
            }
            catch (InvalidOperationException)
            {
                // Not on screen at this instant.
            }
        }

        // MARK: Keyboard

        /// <summary>
        /// Opened with the shortcut: the line takes the keyboard, so photos can be
        /// chosen with the arrows and acted on with a key. Focus goes back to
        /// where it was when the line tucks away.
        /// </summary>
        public void EnterKeyboard()
        {
            if (keyboard || line.LiveCount == 0) return;
            keyboard = true;
            previousForeground = Native.GetForegroundWindow();
            Activatable = true;
            Focusable = true;
            Activate();
            Native.SetForegroundWindow(Handle);
            Focus();
            Keyboard.Focus(this);
            line.SelectedId = line.Live.LastOrDefault()?.Id;
            UpdatePreview();
        }

        public void ExitKeyboard(bool restoreFocus)
        {
            if (!keyboard) return;
            keyboard = false;
            line.SelectedId = null;
            Focusable = false;
            Activatable = false;
            if (restoreFocus && previousForeground != IntPtr.Zero && Native.GetForegroundWindow() == Handle)
                Native.SetForegroundWindow(previousForeground);
            previousForeground = IntPtr.Zero;
            UpdatePreview();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (!keyboard) return;
            var live = line.Live;
            int i = live.FindIndex(x => x.Id == line.SelectedId);
            var selected = i >= 0 ? live[i] : null;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Left:
                    if (i > 0) line.SelectedId = live[i - 1].Id;
                    else if (line.ScrollOlder()) line.SelectedId = line.Live.FirstOrDefault()?.Id;
                    break;
                case Key.Right:
                    if (i >= 0 && i < live.Count - 1) line.SelectedId = live[i + 1].Id;
                    else if (line.ScrollNewer()) line.SelectedId = line.Live.LastOrDefault()?.Id;
                    break;
                case Key.Home:
                    if (live.Count > 0) line.SelectedId = live[0].Id;
                    break;
                case Key.End:
                    if (live.Count > 0) line.SelectedId = live[live.Count - 1].Id;
                    break;
                case Key.Enter:
                case Key.C:
                    if (selected != null) line.Copy(selected.Id);
                    break;
                case Key.E:
                    if (selected != null) line.Edit(selected.Id);
                    break;
                case Key.O:
                    if (selected != null) line.Open(selected.Id);
                    break;
                case Key.P:
                    if (selected != null && cards.TryGetValue(selected.Id, out var card)) RequestPin(card, follow: false);
                    break;
                case Key.Delete:
                case Key.Back:
                    if (selected == null) break;
                    var next = i + 1 < live.Count ? live[i + 1] : i > 0 ? live[i - 1] : null;
                    line.Discard(selected.Id);
                    line.SelectedId = next?.Id;
                    break;
                case Key.Z:
                    if (ctrl) Undo.Perform();
                    break;
                case Key.Escape:
                    CloseRequested?.Invoke();
                    break;
                default:
                    e.Handled = false;
                    break;
            }
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

        /// <summary>Development: shows the big preview for the first photo.</summary>
        public void PreviewFirst()
        {
            var card = cards.Values.FirstOrDefault(c => c.IsTouchable);
            if (card != null && line.Revealed) preview.ShowFor(card.Item, card.PhotoOnScreen(), Monitor, keyboard: false);
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
            // Measured from where the window sits once down, even while it is still sliding.
            double s = Scale;
            var r = new Rect(x - size.Width / 2, top, size.Width, size.Height);
            return new PxRect((int)Math.Round(home.X + r.X * s), (int)Math.Round(home.Y + r.Y * s),
                              (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
        }
    }
}

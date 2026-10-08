using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Pegline.Loc;
using Shapes = System.Windows.Shapes;

namespace Pegline
{
    /// <summary>
    /// One photo with its clip. All the charm lives here: it drops onto the
    /// line, rides the line as it bounces, swings, sways with the breeze,
    /// drips while it is fresh, curls when it is old, and falls when you pull
    /// it off. It also takes the gestures: click copies, press and hold edits,
    /// double click opens, dragging hands the file to another app or folder,
    /// Shift-dragging pins it to the screen, and the corner cross discards.
    ///
    /// The card and its shadow are drawn once into bitmaps and only those
    /// move. Redrawing rounded clips and blurred shadows on every frame of a
    /// bouncing line would keep a whole CPU core busy.
    /// </summary>
    sealed class CardView : Canvas
    {
        public static bool IsDragging { get; private set; }

        /// <summary>A capture this fresh is still wet.</summary>
        const double DryTime = 60;
        /// <summary>A capture this old starts to curl at a corner.</summary>
        static readonly TimeSpan CurlAfter = TimeSpan.FromHours(24);
        /// <summary>How much of the line's slope the photo takes on: it hangs from one clip, not two.</summary>
        const double SlopeFollow = 0.6;
        /// <summary>The largest card, for weighing them against each other.</summary>
        const double LargestArea = (Layout.CardWidth - 14 + 2 * Layout.FrameInset) * (Layout.PhotoMaxHeight + 2 * Layout.FrameInset);
        /// <summary>Room around a baked shadow for its blur.</summary>
        const double ShadowRoom = 24;
        /// <summary>Room around the baked clip for its shadow.</summary>
        const double ClipRoom = 4;

        static readonly Brush WetBrush = Frozen(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x30, 0xC4, 0xE6, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.34),
                new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.43),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.52),
                new GradientStop(Color.FromArgb(0x28, 0xA8, 0xD8, 0xFF), 1),
            }
        });

        static readonly Dictionary<string, BitmapSource> bakedShadows = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<double, BitmapSource> bakedClips = new Dictionary<double, BitmapSource>();

        public Pegged Item { get; }
        readonly Line line;
        readonly LineWindow host;

        readonly Canvas body = new Canvas();
        readonly RotateTransform rotate = new RotateTransform();
        readonly TranslateTransform drop = new TranslateTransform();
        /// <summary>The card as you see it and touch it: the baked face, and the wet sheen while it lasts.</summary>
        readonly Grid card = new Grid();
        readonly Image face = new Image { Stretch = Stretch.Fill };
        readonly ScaleTransform zoomTransform = new ScaleTransform(1, 1);
        readonly Image shade = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        readonly ScaleTransform shadeScale = new ScaleTransform(1, 1);
        readonly TranslateTransform shadeShift = new TranslateTransform();
        readonly Image clip = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        readonly Border sheen;
        readonly Border cross;
        readonly ScaleTransform crossScale = new ScaleTransform(0.6, 0.6, 10, 10);
        readonly Border badge;
        readonly TranslateTransform badgeShift = new TranslateTransform();

        public Motion X { get; }
        readonly Motion swing, arriveY, opacity, zoom, lift, crossT, badgeT;

        bool hovering, pressed, dragging, copied, wasFlying;
        BitmapSource shownThumb;
        Size cardSize, photoSize;
        double swingAngle, slope, curl, bakedScale;

        // Pointer state for the gestures.
        Point? downPoint;
        bool startedDrag, didLongPress;
        DispatcherTimer holdTimer;

        /// <summary>How long you hold before the editor opens. Long enough not to fire on a slow click, short enough to feel deliberate.</summary>
        const double HoldDuration = 0.45;
        /// <summary>The discard cross in the top left corner of the card.</summary>
        const double CrossHitSize = 26;

        public CardView(Pegged item, Line line, LineWindow host)
        {
            Item = item;
            this.line = line;
            this.host = host;
            Width = Layout.CardWidth;
            Height = Layout.PanelHeight;

            var transforms = new TransformGroup();
            transforms.Children.Add(rotate);
            transforms.Children.Add(drop);
            rotate.CenterX = Layout.CardWidth / 2;
            body.RenderTransform = transforms;
            body.Width = Layout.CardWidth;
            body.Height = Layout.PanelHeight;
            Children.Add(body);

            var shadeTransforms = new TransformGroup();
            shadeTransforms.Children.Add(shadeScale);
            shadeTransforms.Children.Add(shadeShift);
            shade.RenderTransform = shadeTransforms;
            body.Children.Add(shade);

            // Moving bitmaps only ever turn a degree or two, where smooth
            // bilinear sampling looks the same as the costly filter.
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.Linear);
            RenderOptions.SetBitmapScalingMode(shade, BitmapScalingMode.Linear);
            RenderOptions.SetBitmapScalingMode(clip, BitmapScalingMode.Linear);
            sheen = new Border
            {
                CornerRadius = new CornerRadius(Layout.FrameRadius - Layout.FrameInset),
                Margin = new Thickness(Layout.FrameInset),
                Background = WetBrush,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            card.Children.Add(face);
            card.Children.Add(sheen);
            card.RenderTransform = zoomTransform;
            Canvas.SetTop(card, Layout.CardOffsetBelowTop);
            body.Children.Add(card);

            // Drawn here, clicked through the card, which handles the corner.
            cross = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                IsHitTestVisible = false,
                RenderTransform = crossScale,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = Visuals.IconFont,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Palette.Primary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            body.Children.Add(cross);

            badge = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 11, 5),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                IsHitTestVisible = false,
                RenderTransform = badgeShift,
                Visibility = Visibility.Collapsed,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "",
                            FontFamily = Visuals.IconFont,
                            FontSize = 10,
                            Foreground = Palette.Primary,
                            Margin = new Thickness(0, 1, 6, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = L("Copied", "Copiado", "Copié"),
                            FontFamily = Visuals.UiFont,
                            FontSize = 11.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Palette.Primary,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            body.Children.Add(badge);

            Canvas.SetLeft(clip, Layout.CardWidth / 2 - 4.5 - ClipRoom);
            Canvas.SetTop(clip, -ClipRoom);
            clip.Width = 9 + 2 * ClipRoom;
            clip.Height = 26 + 2 * ClipRoom;
            body.Children.Add(clip);

            X = new Motion(0, PlaceAt, 0.05);
            swing = new Motion(0, v =>
            {
                swingAngle = v;
                ApplyAngle();
            }, 0.01);
            arriveY = new Motion(0, v => drop.Y = v, 0.05);
            opacity = new Motion(0, v => body.Opacity = Math.Max(0, Math.Min(1, v)), 0.002);
            zoom = new Motion(1, v =>
            {
                zoomTransform.ScaleX = zoomTransform.ScaleY = v;
                shadeScale.ScaleX = shadeScale.ScaleY = v;
            }, 0.0005);
            // Lifting off on hover: the shadow drops further and darkens.
            lift = new Motion(0, v =>
            {
                shadeShift.Y = Easing.Lerp(5, 8, v);
                shade.Opacity = Easing.Lerp(0.26, 0.36, v);
            }, 0.002);
            crossT = new Motion(0, v =>
            {
                cross.Opacity = v;
                cross.Visibility = v > 0.001 ? Visibility.Visible : Visibility.Collapsed;
                crossScale.ScaleX = crossScale.ScaleY = 0.6 + 0.4 * v;
            }, 0.002);
            badgeT = new Motion(0, v =>
            {
                badge.Opacity = v;
                badge.Visibility = v > 0.001 ? Visibility.Visible : Visibility.Collapsed;
                badgeShift.Y = -4 * (1 - v);
            }, 0.002);

            card.MouseLeftButtonDown += OnDown;
            card.MouseMove += OnMove;
            card.MouseLeftButtonUp += OnUp;
            card.MouseRightButtonUp += OnRightUp;

            Update();
            Age(DateTime.UtcNow);
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        // MARK: Layout and baking

        void Resize()
        {
            photoSize = Layout.PhotoSize(Item.Thumb.PixelWidth, Item.Thumb.PixelHeight);
            cardSize = Layout.CardSize(Item.Thumb.PixelWidth, Item.Thumb.PixelHeight);
            card.Width = cardSize.Width;
            card.Height = cardSize.Height;
            double left = (Layout.CardWidth - cardSize.Width) / 2;
            Canvas.SetLeft(card, left);
            zoomTransform.CenterX = cardSize.Width / 2;

            Canvas.SetLeft(shade, left - ShadowRoom);
            Canvas.SetTop(shade, Layout.CardOffsetBelowTop - ShadowRoom);
            shade.Width = cardSize.Width + 2 * ShadowRoom;
            shade.Height = cardSize.Height + 2 * ShadowRoom;
            shadeScale.CenterX = cardSize.Width / 2 + ShadowRoom;
            shadeScale.CenterY = ShadowRoom;

            Canvas.SetLeft(cross, left + 3);
            Canvas.SetTop(cross, Layout.CardOffsetBelowTop + 3);
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var b = badge.DesiredSize;
            Canvas.SetLeft(badge, (Layout.CardWidth - b.Width) / 2);
            Canvas.SetTop(badge, Layout.CardOffsetBelowTop + cardSize.Height - b.Height + 16);
            Bake();
        }

        /// <summary>
        /// Draws the card's face, its shadow and the clip into bitmaps at the
        /// display's scale. Again when the photo, the theme, the curl or the
        /// display change; never while it moves.
        /// </summary>
        public void Bake()
        {
            if (cardSize.Width <= 0) return;
            double scale = host.Scale > 0 ? host.Scale : 1;
            bakedScale = scale;
            face.Source = Render(BuildFace(), cardSize, scale);
            shade.Source = ShadowFor(cardSize, curl, scale);
            clip.Source = ClipFor(scale);
        }

        /// <summary>Re-bakes if the line moved to a display with another scale.</summary>
        public void CheckScale()
        {
            if (Math.Abs(host.Scale - bakedScale) > 0.01) Bake();
        }

        FrameworkElement BuildFace()
        {
            double inner = Layout.FrameRadius - Layout.FrameInset;
            var photo = new Image
            {
                Source = Item.Thumb,
                Width = photoSize.Width,
                Height = photoSize.Height,
                Stretch = Stretch.Fill,
                // Concentric corners: the photo's radius is the frame's minus the inset.
                Clip = new RectangleGeometry(new Rect(photoSize), inner, inner)
            };
            RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.HighQuality);
            var face = new Grid { Width = cardSize.Width, Height = cardSize.Height };
            face.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Layout.FrameRadius),
                Background = Palette.Glass,
                Child = new Grid
                {
                    Margin = new Thickness(Layout.FrameInset),
                    Children =
                    {
                        photo,
                        new Border
                        {
                            CornerRadius = new CornerRadius(inner),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                            BorderThickness = new Thickness(0.5)
                        }
                    }
                }
            });
            face.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Layout.FrameRadius),
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75)
            });
            face.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Layout.FrameRadius),
                BorderBrush = Palette.Outline,
                BorderThickness = new Thickness(0.5)
            });

            double c = CurlSize;
            if (c > 0)
            {
                // The corner folded over: everything beyond the fold is gone, and
                // the back of the print shows, casting a little shadow.
                double w = cardSize.Width, h = cardSize.Height;
                var fold = new PathGeometry(new[]
                {
                    new PathFigure(new Point(w - c, h), new PathSegment[] { new LineSegment(new Point(w, h - c), true), new LineSegment(new Point(w - c, h - c), true) }, true)
                });
                face.Clip = Shape(cardSize, c);
                bool dark = Theme.AppsDark;
                face.Children.Add(new Shapes.Path
                {
                    Data = fold,
                    Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    RenderTransform = new TranslateTransform(-1.5, -1.5)
                });
                face.Children.Add(new Shapes.Path
                {
                    Data = fold,
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                    StrokeThickness = 0.5,
                    Fill = new LinearGradientBrush(
                        dark ? Color.FromRgb(214, 214, 210) : Color.FromRgb(250, 250, 247),
                        dark ? Color.FromRgb(168, 168, 164) : Color.FromRgb(214, 214, 209),
                        new Point(0.6, 0.6), new Point(0, 0))
                });
            }
            return face;
        }

        double CurlSize => Math.Min(curl, Math.Min(cardSize.Width, cardSize.Height) * 0.4);

        /// <summary>The card's outline: a rounded rectangle, less the folded corner if it has curled.</summary>
        static Geometry Shape(Size size, double curl)
        {
            var rounded = new RectangleGeometry(new Rect(size), Layout.FrameRadius, Layout.FrameRadius);
            if (curl <= 0) return rounded;
            double w = size.Width, h = size.Height;
            var beyond = new PathGeometry(new[]
            {
                new PathFigure(new Point(w - curl, h + 30), new PathSegment[]
                {
                    new LineSegment(new Point(w - curl, h), false), new LineSegment(new Point(w, h - curl), false),
                    new LineSegment(new Point(w + 30, h - curl), false), new LineSegment(new Point(w + 30, h + 30), false)
                }, true)
            });
            return new CombinedGeometry(GeometryCombineMode.Exclude, rounded, beyond);
        }

        static BitmapSource Render(FrameworkElement element, Size size, double scale)
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
                                                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(element);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>A soft blur of the card's shape, shared by every card of the same size.</summary>
        static BitmapSource ShadowFor(Size size, double curl, double scale)
        {
            string key = $"{size.Width:0.0}x{size.Height:0.0}/{curl:0}/{scale:0.00}";
            if (bakedShadows.TryGetValue(key, out var cached)) return cached;
            var shape = new Shapes.Path
            {
                Data = Shape(size, Math.Min(curl, Math.Min(size.Width, size.Height) * 0.4)),
                Fill = Brushes.Black,
                Margin = new Thickness(ShadowRoom),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var room = new Grid { Children = { shape }, Effect = new BlurEffect { Radius = 18, KernelType = KernelType.Gaussian } };
            var bitmap = Render(room, new Size(size.Width + 2 * ShadowRoom, size.Height + 2 * ShadowRoom), scale);
            if (bakedShadows.Count > 64) bakedShadows.Clear();
            bakedShadows[key] = bitmap;
            return bitmap;
        }

        static BitmapSource ClipFor(double scale)
        {
            if (bakedClips.TryGetValue(scale, out var cached)) return cached;
            var pin = Visuals.Clothespin();
            pin.Margin = new Thickness(ClipRoom);
            var room = new Grid { Children = { pin } };
            var bitmap = Render(room, new Size(9 + 2 * ClipRoom, 26 + 2 * ClipRoom), scale);
            bakedClips[scale] = bitmap;
            return bitmap;
        }

        /// <summary>Moves the photo along the line to x. It hangs wherever the line is there.</summary>
        void PlaceAt(double x)
        {
            CurrentX = x;
            Pose();
            // Its weight moved with it.
            host.Rope.Wake();
        }

        /// <summary>The line moved under the photo: follow it, height and angle.</summary>
        public void FollowRope() => Pose();

        void Pose()
        {
            var rope = host.Rope;
            Canvas.SetLeft(this, CurrentX - Layout.CardWidth / 2);
            Canvas.SetTop(this, rope.YAt(CurrentX) - Layout.PinAbove);
            slope = rope.SlopeAt(CurrentX);
            ApplyAngle();
        }

        void ApplyAngle() => rotate.Angle = swingAngle + Item.Tilt + slope * SlopeFollow;

        public double CurrentX { get; private set; }

        /// <summary>Whether the photo pulls on the line: not while it is still flying in or already falling.</summary>
        public bool CarriesWeight => !Item.Falling && !Item.Flying;

        /// <summary>Bigger photos are a little heavier.</summary>
        public double Weight => 0.7 + 0.3 * cardSize.Width * cardSize.Height / LargestArea;

        /// <summary>The card's frame in the line's coordinates, for pointer hit testing.</summary>
        public Rect CardRect
        {
            get
            {
                double top = host.Rope.YAt(CurrentX) - Layout.PinAbove + Layout.CardOffsetBelowTop + drop.Y;
                return new Rect(CurrentX - cardSize.Width / 2, top, cardSize.Width, cardSize.Height);
            }
        }

        public bool IsTouchable => !Item.Falling && !Item.Flying;

        // MARK: Motion

        public void Appear(double x)
        {
            X.Set(x);
            wasFlying = Item.Flying;
            // A capture that flies in is already in place; the flight does the arriving.
            if (Item.Flying)
            {
                opacity.Set(0);
                return;
            }
            swing.Set(16);
            swing.SpringWith(0, 46, 2.6);
            arriveY.Set(-46);
            arriveY.Spring(0, 0.42, 0.72);
            opacity.Set(0);
            opacity.Spring(1, 0.42, 0.72);
        }

        public void MoveTo(double x) => X.Spring(x, 0.55, 0.78);

        public void Update()
        {
            if (!ReferenceEquals(shownThumb, Item.Thumb))
            {
                shownThumb = Item.Thumb;
                Resize();
            }
            if (Item.Falling)
            {
                // The fall itself is drawn over the whole screen, so the card here just steps aside at once.
                IsHitTestVisible = false;
                opacity.Set(0);
                return;
            }
            if (wasFlying && !Item.Flying)
            {
                // Landing after the flight: no jump, just a small sway from rest.
                wasFlying = false;
                opacity.Tween(1, 0.16, Ease.Out);
                Nudge(2.2);
            }
        }

        /// <summary>A gust reaching this photo: it swings away from the wind, then settles.</summary>
        public void Breeze(double delay, double degrees) => Delay.Run(delay, () => Nudge(degrees));

        /// <summary>The pointer swept past: a push in degrees per second.</summary>
        public void Blow(double velocity) => swing.Kick(velocity, 0, 38, 2.4, 9);

        void Nudge(double degrees)
        {
            swing.Tween(degrees, 0.3, Ease.Out, () => swing.SpringWith(0, 38, 2.4));
        }

        public void RefreshState()
        {
            bool h = line.HoveredId == Item.Id && !Item.Falling;
            bool p = line.PressedId == Item.Id;
            bool d = line.DraggingId == Item.Id;
            bool c = line.CopiedId == Item.Id;

            if (p != pressed)
            {
                pressed = p;
                // Holding presses the photo in slowly, so a long press feels like it is building up to something.
                if (p) zoom.Tween(0.95, 0.45, Ease.InOut);
                else zoom.Spring(h ? 1.035 : 1, 0.3, 0.6);
            }
            if (h != hovering)
            {
                hovering = h;
                if (!pressed) zoom.Spring(h ? 1.035 : 1, 0.3, 0.6);
                lift.Tween(h ? 1 : 0, 0.18, Ease.Out);
            }
            if (d != dragging)
            {
                dragging = d;
                card.Opacity = d ? 0.45 : 1;
                shade.Visibility = d ? Visibility.Hidden : Visibility.Visible;
            }
            double crossTarget = hovering && !dragging ? 1 : 0;
            if (crossT.Target != crossTarget) crossT.Tween(crossTarget, 0.18, Ease.Out);
            if (c != copied)
            {
                copied = c;
                badgeT.Tween(c ? 1 : 0, 0.2, Ease.Out);
                if (c) Nudge(3);
            }
        }

        // MARK: Drying

        /// <summary>From 1 just after the capture to 0 a minute later.</summary>
        public double Wetness { get; private set; }

        /// <summary>
        /// Fresh captures are still wet: a cool sheen with a highlight that
        /// fades as they dry. A day later the print starts to curl at a corner,
        /// a little more each day, a quiet hint to keep it or let it go.
        /// </summary>
        public void Age(DateTime now)
        {
            var age = now - Item.Created;
            double wet = Math.Max(0, Math.Min(1, 1 - age.TotalSeconds / DryTime));
            if (Math.Abs(wet - Wetness) > 0.004 || (wet == 0 && Wetness != 0))
            {
                Wetness = wet;
                sheen.Opacity = wet;
                sheen.Visibility = wet > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            double c = age < CurlAfter ? 0 : Math.Round(20 + 10 * Math.Min(1, (age - CurlAfter).TotalHours / 72));
            if (c != curl)
            {
                curl = c;
                Bake();
            }
        }

        /// <summary>Where a drop falls from: the bottom edge, somewhere under the photo.</summary>
        public Point DripPoint(Random random)
        {
            var r = CardRect;
            double x = CurrentX + (random.NextDouble() - 0.5) * Math.Max(0, photoSize.Width - 20);
            return new Point(x, r.Bottom - 1);
        }

        // MARK: Gestures

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var local = e.GetPosition(card);
            if (local.X < CrossHitSize && local.Y < CrossHitSize)
            {
                downPoint = null;
                line.Discard(Item.Id);
                return;
            }
            if (e.ClickCount == 2)
            {
                downPoint = null;
                EndPress();
                line.Open(Item.Id);
                return;
            }

            downPoint = e.GetPosition(host);
            startedDrag = false;
            didLongPress = false;
            line.PressedId = Item.Id;
            card.CaptureMouse();
            holdTimer?.Stop();
            holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HoldDuration) };
            holdTimer.Tick += (s, a) =>
            {
                holdTimer?.Stop();
                if (downPoint == null || startedDrag) return;
                didLongPress = true;
                line.PressedId = null;
                line.Edit(Item.Id);
            };
            holdTimer.Start();
        }

        void EndPress()
        {
            holdTimer?.Stop();
            holdTimer = null;
            if (line.PressedId == Item.Id) line.PressedId = null;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (downPoint == null || startedDrag || didLongPress || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(host);
            if ((p - downPoint.Value).Length <= 4) return;
            if (Native.IsDown(Native.VK_SHIFT))
            {
                // Shift: pull it off the line and stick it on the screen.
                startedDrag = true;
                EndPress();
                card.ReleaseMouseCapture();
                downPoint = null;
                host.RequestPin(this, follow: true);
                return;
            }
            BeginDrag();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            card.ReleaseMouseCapture();
            EndPress();
            if (downPoint != null && !startedDrag && !didLongPress && e.ClickCount == 1) line.Copy(Item.Id);
            downPoint = null;
            didLongPress = false;
        }

        /// <summary>
        /// Hands the file to Windows drag and drop. Each destination means one thing:
        /// an app gets a copy and the photo stays; a folder on the same drive
        /// keeps the file and the photo leaves the line; the Recycle Bin discards
        /// it; nowhere that accepts it, and the photo flies back.
        /// </summary>
        void BeginDrag()
        {
            startedDrag = true;
            EndPress();
            card.ReleaseMouseCapture();

            var ghost = new DragGhost(Item.Thumb, PhotoOnScreen(), host.Scale);
            IsDragging = true;
            line.DraggingId = Item.Id;
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { Item.Path });

            var result = DragDropEffects.None;
            try
            {
                result = DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch (Exception e)
            {
                Log.Error("Drag failed", e);
            }
            finally
            {
                IsDragging = false;
                startedDrag = false;
                downPoint = null;
            }
            Log.Info("Drag ended with " + result);

            bool stillHere = File.Exists(Item.Path);
            ghost.End(result == DragDropEffects.None && stillHere ? PhotoOnScreen() : (PxRect?)null);
            line.DraggingId = null;
            // Moved into a folder: it is saved where you wanted it. Explorer
            // may finish a move a moment later, so look again then.
            line.Prune();
            Delay.Run(0.6, line.Prune);
        }

        /// <summary>The photo's area on screen, in pixels.</summary>
        public PxRect PhotoOnScreen()
        {
            double inset = Layout.FrameInset;
            var a = card.PointToScreen(new Point(inset, inset));
            var b = card.PointToScreen(new Point(inset + photoSize.Width, inset + photoSize.Height));
            return PxRect.FromLTRB((int)Math.Round(Math.Min(a.X, b.X)), (int)Math.Round(Math.Min(a.Y, b.Y)),
                                   (int)Math.Round(Math.Max(a.X, b.X)), (int)Math.Round(Math.Max(a.Y, b.Y)));
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var id = Item.Id;
            bool inInbox = line.IsInInbox(id);
            var menu = new ContextMenu();
            menu.Items.Add(Menus.Item(L("Copy", "Copiar", "Copier"), () => line.Copy(id)));
            menu.Items.Add(Menus.Item(L("Open", "Abrir", "Ouvrir"), () => line.Open(id)));
            menu.Items.Add(Menus.Item(L("Edit", "Editar", "Modifier"), () => line.Edit(id)));
            menu.Items.Add(Menus.Item(L("Pin to screen", "Fijar en la pantalla", "Épingler à l’écran"), () => host.RequestPin(this, follow: false),
                                      gesture: L("Shift+drag", "Mayús+arrastrar", "Maj+glisser")));
            menu.Items.Add(Menus.Item(L("Show in Explorer", "Mostrar en el Explorador", "Afficher dans l’Explorateur"), () => line.Reveal(id)));
            if (inInbox)
                menu.Items.Add(Menus.Item(L("Save to Screenshots", "Guardar en Capturas", "Enregistrer dans Captures d’écran"), () => line.Save(id)));
            menu.Items.Add(new Separator());
            if (inInbox)
            {
                menu.Items.Add(Menus.Item(L("Discard", "Descartar", "Jeter"), () => line.Discard(id)));
            }
            else
            {
                menu.Items.Add(Menus.Item(L("Take down", "Descolgar", "Décrocher"), () => line.Discard(id)));
                menu.Items.Add(Menus.Item(L("Move to Recycle Bin", "Mover a la Papelera de reciclaje", "Placer dans la Corbeille"), () => line.Trash(id)));
            }
            Menus.Show(menu);
        }
    }
}

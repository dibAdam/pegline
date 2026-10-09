using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Pegline.Loc;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>
    /// Photos taken off the line and stuck on the screen like fridge magnets.
    /// They float above your work, never take focus, come back after a
    /// restart, and go back on the line when you drag them to the top edge.
    /// </summary>
    sealed class PinBoard
    {
        const string StoreKey = "Pins";

        readonly List<PinWindow> pins = new List<PinWindow>();
        readonly Action<string, PxRect> hangBack;
        readonly DispatcherTimer watch;

        public Line Line { get; }

        /// <param name="hangBack">Puts a file back on the line, flying from where the pin was.</param>
        public PinBoard(Line line, Action<string, PxRect> hangBack)
        {
            Line = line;
            this.hangBack = hangBack;
            watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            watch.Tick += (s, e) => { foreach (var pin in pins.ToList()) pin.Check(); };
            Restore();
        }

        /// <param name="from">Where the photo is on screen now, so the pin grows out of it.</param>
        /// <param name="follow">Keep it under the pointer until the button is released.</param>
        public void Pin(string path, PxRect from, bool follow)
        {
            var pin = PinWindow.Create(this, path, from, follow);
            if (pin == null) return;
            pins.Add(pin);
            watch.Start();
            Save();
            Log.Info("Pinned " + System.IO.Path.GetFileName(path));
        }

        internal void Removed(PinWindow pin)
        {
            pins.Remove(pin);
            if (pins.Count == 0) watch.Stop();
            Save();
        }

        internal void HangBack(PinWindow pin)
        {
            var rect = pin.PhotoOnScreen;
            pin.Close();
            Removed(pin);
            hangBack(pin.Path, rect);
        }

        internal void Save() => Settings.SetStrings(StoreKey, pins.Select(p => p.Describe()).ToArray());

        void Restore()
        {
            foreach (var entry in Settings.GetStrings(StoreKey))
            {
                var pin = PinWindow.Restore(this, entry);
                if (pin != null) pins.Add(pin);
            }
            if (pins.Count > 0) watch.Start();
        }

        /// <summary>Puts a pin back where it was, for Undo.</summary>
        internal void Repin(string entry)
        {
            var pin = PinWindow.Restore(this, entry);
            if (pin == null) return;
            pins.Add(pin);
            watch.Start();
            Save();
        }
    }

    sealed class PinWindow : OverlayWindow
    {
        /// <summary>Room around the photo for the shadow, and above it for the magnet.</summary>
        const double Gap = 22, MagnetRoom = 12, Inset = 4, Radius = 12;

        static readonly Color[] MagnetColors =
        {
            Color.FromRgb(0x14, 0xB8, 0xA6), // teal
            Color.FromRgb(0xF9, 0x73, 0x62), // coral
            Color.FromRgb(0xF5, 0xB0, 0x2E), // sunflower
            Color.FromRgb(0x9B, 0x7B, 0xF6), // lilac
            Color.FromRgb(0x38, 0xB2, 0xF0), // sky
            Color.FromRgb(0x4C, 0xC9, 0x6E), // leaf
        };
        static readonly Random random = new Random();

        readonly PinBoard board;
        readonly int magnet;
        readonly Grid root = new Grid();
        readonly Image picture = new Image { Stretch = Stretch.Fill };
        readonly DropShadowEffect shadow = new DropShadowEffect { Color = Colors.Black, Direction = 270, BlurRadius = 18, ShadowDepth = 6, Opacity = 0.3 };
        readonly Border cross, badge;
        readonly Motion crossT, lift, badgeT;

        /// <summary>The photo's area on screen, in pixels. Everything else is laid out around it.</summary>
        Rect photo;
        double fade = 1;
        int imageWidth, imageHeight;
        DateTime lastWrite;
        bool hidden, moving, nearTop;
        DispatcherTimer moveTimer;
        Motion grow;

        public string Path { get; private set; }

        PinWindow(PinBoard board, string path, BitmapSource image, int width, int height, int magnet)
            : base(clickThrough: false)
        {
            this.board = board;
            this.magnet = magnet;
            Path = path;
            imageWidth = width;
            imageHeight = height;
            lastWrite = LastWriteOf(path);

            picture.Source = image;
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            picture.SizeChanged += (s, e) =>
                picture.Clip = new RectangleGeometry(new Rect(e.NewSize), Radius - Inset, Radius - Inset);

            var frameMargin = new Thickness(Gap, Gap + MagnetRoom, Gap, Gap);
            var frame = new Border
            {
                Margin = frameMargin,
                CornerRadius = new CornerRadius(Radius),
                Background = Palette.Glass,
                Padding = new Thickness(Inset),
                Child = picture,
                Effect = shadow
            };
            root.Children.Add(frame);
            root.Children.Add(new Border
            {
                Margin = frameMargin,
                CornerRadius = new CornerRadius(Radius),
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                IsHitTestVisible = false
            });
            root.Children.Add(new Border
            {
                Margin = new Thickness(Gap - 0.5, Gap + MagnetRoom - 0.5, Gap - 0.5, Gap - 0.5),
                CornerRadius = new CornerRadius(Radius + 0.5),
                BorderBrush = Palette.Outline,
                BorderThickness = new Thickness(0.5),
                IsHitTestVisible = false
            });
            root.Children.Add(Magnet(MagnetColors[magnet % MagnetColors.Length]));

            cross = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Gap + 5, Gap + MagnetRoom + 5, 0, 0),
                Opacity = 0,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = Visuals.IconFont,
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Palette.Primary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            root.Children.Add(cross);

            badge = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 11, 5),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, Gap + 10),
                Opacity = 0,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = L("Copied", "Copiado", "Copié"),
                    FontFamily = Visuals.UiFont,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Palette.Primary
                }
            };
            root.Children.Add(badge);

            Content = root;
            crossT = new Motion(0, v => cross.Opacity = v, 0.002);
            badgeT = new Motion(0, v => badge.Opacity = v, 0.002);
            lift = new Motion(0, v =>
            {
                shadow.BlurRadius = Easing.Lerp(18, 28, v);
                shadow.ShadowDepth = Easing.Lerp(6, 12, v);
                shadow.Opacity = Easing.Lerp(0.3, 0.38, v);
            }, 0.002);

            MouseEnter += (s, e) => crossT.Tween(1, 0.15, Ease.Out);
            MouseLeave += (s, e) => { if (!moving) crossT.Tween(0, 0.2, Ease.Out); };
            MouseLeftButtonDown += OnDown;
            MouseRightButtonUp += OnRightUp;
            MouseWheel += OnWheel;
        }

        /// <summary>A round fridge magnet: glossy, colored, with a soft shadow.</summary>
        static FrameworkElement Magnet(Color color)
        {
            Color Shade(double f) => Color.FromRgb((byte)(color.R * f), (byte)(color.G * f), (byte)(color.B * f));
            Color Tint(double f) => Color.FromRgb((byte)(color.R + (255 - color.R) * f), (byte)(color.G + (255 - color.G) * f), (byte)(color.B + (255 - color.B) * f));
            var disc = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new RadialGradientBrush(Tint(0.35), Shade(0.82)) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.42, 0.38), RadiusX = 0.7, RadiusY = 0.7 },
                Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                StrokeThickness = 0.6
            };
            var gloss = new Ellipse
            {
                Width = 10,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                Margin = new Thickness(5, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new RotateTransform(-25, 5, 3)
            };
            return new Grid
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Gap + MagnetRoom - 13, 0, 0),
                IsHitTestVisible = false,
                Children = { disc, gloss },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2.5, Direction = 270, Opacity = 0.4 }
            };
        }

        // MARK: Making and restoring

        public static PinWindow Create(PinBoard board, string path, PxRect from, bool follow)
        {
            var image = Imaging.LoadThumbnail(path, 1600, out int w, out int h);
            if (image == null) return null;
            var pin = new PinWindow(board, path, image, w, h, random.Next(MagnetColors.Length));
            var monitor = Monitors.At(from.Center);
            var size = PinnedSize(w, h, monitor.Scale);
            var start = new Rect(from.X, from.Y, from.Width, from.Height);

            pin.Place(start);
            pin.Show();
            pin.ApplyBounds();

            if (follow)
            {
                pin.FollowPointer(start, size);
            }
            else
            {
                // It drops below the line and snaps on with a little overshoot.
                var work = monitor.WorkArea;
                double s = monitor.Scale;
                double x = Math.Max(work.X + 24 * s, Math.Min(work.Right - size.Width - 24 * s, from.Center.X - size.Width / 2));
                double y = Math.Min(work.Bottom - size.Height - 24 * s, work.Y + (Layout.PanelHeight + 36) * s);
                pin.GrowTo(start, new Rect(x, y, size.Width, size.Height));
            }
            return pin;
        }

        /// <summary>Larger than on the line, at most 460 by 340 points, and never larger than the image itself.</summary>
        static Size PinnedSize(int width, int height, double scale)
        {
            double fit = Math.Min(Math.Min(460 * scale / width, 340 * scale / height), 1);
            fit = Math.Max(fit, Math.Min(140 * scale / width, 1));
            return new Size(Math.Round(width * fit), Math.Round(height * fit));
        }

        public static PinWindow Restore(PinBoard board, string entry)
        {
            try
            {
                var parts = entry.Split('\t');
                if (parts.Length < 7 || !File.Exists(parts[0])) return null;
                var c = CultureInfo.InvariantCulture;
                var rect = new Rect(double.Parse(parts[1], c), double.Parse(parts[2], c), double.Parse(parts[3], c), double.Parse(parts[4], c));
                var image = Imaging.LoadThumbnail(parts[0], 1600, out int w, out int h);
                if (image == null) return null;
                var pin = new PinWindow(board, parts[0], image, w, h, int.Parse(parts[6], c));
                pin.fade = Math.Max(0.3, Math.Min(1, double.Parse(parts[5], c)));
                pin.root.Opacity = pin.fade;
                // A display that is no longer there: bring it back where it can be seen.
                var center = new POINT((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
                if (!Monitors.All.Any(m => m.WorkArea.Contains(center)))
                {
                    var work = Monitors.Primary.WorkArea;
                    rect.X = work.X + (work.Width - rect.Width) / 2;
                    rect.Y = work.Y + (work.Height - rect.Height) / 2;
                }
                pin.Place(rect);
                pin.Show();
                pin.ApplyBounds();
                return pin;
            }
            catch (Exception e)
            {
                Log.Error("Could not restore a pin", e);
                return null;
            }
        }

        public string Describe()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("\t", Path, photo.X.ToString("0", c), photo.Y.ToString("0", c), photo.Width.ToString("0", c),
                               photo.Height.ToString("0", c), fade.ToString("0.00", c), magnet.ToString(c));
        }

        // MARK: Placement

        public PxRect PhotoOnScreen => new PxRect((int)Math.Round(photo.X), (int)Math.Round(photo.Y),
                                                  (int)Math.Round(photo.Width), (int)Math.Round(photo.Height));

        /// <summary>Puts the photo at a screen rectangle; the window grows around it for the frame, shadow and magnet.</summary>
        void Place(Rect r)
        {
            photo = r;
            var center = new POINT((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            double s = Monitors.At(center).Scale;
            double side = (Gap + Inset) * s, top = (Gap + MagnetRoom + Inset) * s;
            SetPixelBounds(new PxRect((int)Math.Round(r.X - side), (int)Math.Round(r.Y - top),
                                      (int)Math.Round(r.Width + 2 * side), (int)Math.Round(r.Height + top + side)));
        }

        static Rect Lerp(Rect a, Rect b, double k) => new Rect(
            Easing.Lerp(a.X, b.X, k), Easing.Lerp(a.Y, b.Y, k),
            Math.Max(8, Easing.Lerp(a.Width, b.Width, k)), Math.Max(8, Easing.Lerp(a.Height, b.Height, k)));

        void GrowTo(Rect from, Rect to)
        {
            bool snapped = false;
            grow = new Motion(0, k =>
            {
                Place(Lerp(from, to, k));
                if (!snapped && k >= 1)
                {
                    snapped = true;
                    if (board.Line.SoundOn) Sounds.Clack();
                }
            }, 0.0005);
            grow.Spring(1, 0.5, 0.62, () => board.Save());
        }

        /// <summary>Shift-dragged off the line: it grows to its pinned size under the pointer and sticks where the button is released.</summary>
        void FollowPointer(Rect from, Size size)
        {
            var cursor = Cursor();
            var anchor = new Vector((cursor.X - from.X) / from.Width, (cursor.Y - from.Y) / from.Height);
            double k = 0;
            grow = new Motion(0, v => k = v, 0.001);
            grow.Tween(1, 0.28, Ease.Out);
            StartMoving(() =>
            {
                var c = Cursor();
                double w = Easing.Lerp(from.Width, size.Width, k), h = Easing.Lerp(from.Height, size.Height, k);
                return new Rect(c.X - anchor.X * w, c.Y - anchor.Y * h, w, h);
            });
        }

        // MARK: Moving

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var p = e.GetPosition(root);
            // Only the cross itself discards, so grabbing a corner to move the pin never does.
            if (p.X >= Gap + 3 && p.X < Gap + 29 && p.Y >= Gap + MagnetRoom + 3 && p.Y < Gap + MagnetRoom + 29)
            {
                Discard();
                return;
            }
            if (e.ClickCount == 2)
            {
                Shell.Open(Path);
                return;
            }
            var cursor = Cursor();
            var offset = new Vector(cursor.X - photo.X, cursor.Y - photo.Y);
            var size = photo.Size;
            StartMoving(() =>
            {
                var c = Cursor();
                return new Rect(c.X - offset.X, c.Y - offset.Y, size.Width, size.Height);
            });
        }

        /// <summary>
        /// Follows the pointer until the button comes up. The button is read
        /// directly, so the pin keeps up however fast the pointer moves.
        /// </summary>
        void StartMoving(Func<Rect> where)
        {
            moving = true;
            lift.Tween(1, 0.15, Ease.Out);
            moveTimer?.Stop();
            moveTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
            moveTimer.Tick += (s, e) =>
            {
                if (!IsDown(VK_LBUTTON))
                {
                    EndMoving();
                    return;
                }
                var r = where();
                if (Math.Abs(r.X - photo.X) > 0.5 || Math.Abs(r.Y - photo.Y) > 0.5 || Math.Abs(r.Width - photo.Width) > 0.5)
                    Place(r);
                // Near the top edge it shrinks back a little: let go to hang it on the line.
                bool top = AtTop(Cursor());
                if (top != nearTop)
                {
                    nearTop = top;
                    root.Opacity = fade * (top ? 0.6 : 1);
                }
            };
            moveTimer.Start();
        }

        void EndMoving()
        {
            moveTimer?.Stop();
            moveTimer = null;
            moving = false;
            lift.Tween(0, 0.25, Ease.Out);
            if (!IsMouseOver) crossT.Tween(0, 0.2, Ease.Out);
            if (nearTop)
            {
                nearTop = false;
                board.HangBack(this);
                return;
            }
            if (board.Line.SoundOn) Sounds.Clack();
            board.Save();
        }

        static bool AtTop(POINT cursor)
        {
            var m = Monitors.At(cursor);
            return cursor.Y < m.Bounds.Y + 28 * m.Scale;
        }

        // MARK: Wheel: size and fade

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (IsDown(VK_CONTROL))
            {
                fade = Math.Max(0.3, Math.Min(1, fade + Math.Sign(e.Delta) * 0.1));
                root.Opacity = fade;
                board.Save();
                return;
            }
            var cursor = Cursor();
            var monitor = Monitors.At(cursor);
            double factor = Math.Pow(1.12, e.Delta / 120.0);
            double min = 110 * monitor.Scale / photo.Width;
            double max = Math.Min(imageWidth * 2.0, monitor.WorkArea.Width * 0.95) / photo.Width;
            factor = Math.Max(min, Math.Min(max, factor));
            if (Math.Abs(factor - 1) < 0.001) return;
            // The point under the pointer stays put, like zooming a map.
            double rx = (cursor.X - photo.X) / photo.Width, ry = (cursor.Y - photo.Y) / photo.Height;
            double w = photo.Width * factor, h = photo.Height * factor;
            Place(new Rect(cursor.X - rx * w, cursor.Y - ry * h, w, h));
            board.Save();
        }

        // MARK: Actions

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            bool inInbox = Inbox.Contains(Path);
            var menu = new ContextMenu();
            menu.Items.Add(Menus.Item(L("Copy", "Copiar", "Copier"), Copy));
            menu.Items.Add(Menus.Item(L("Open", "Abrir", "Ouvrir"), () => Shell.Open(Path)));
            menu.Items.Add(Menus.Item(L("Mark up", "Marcar", "Annoter"), () => MarkupWindow.Open(Path, null)));
            menu.Items.Add(Menus.Item(L("Edit in another app", "Editar en otra app", "Modifier dans une autre app"), () => Shell.Edit(Path)));
            menu.Items.Add(Menus.Item(L("Show in Explorer", "Mostrar en el Explorador", "Afficher dans l’Explorateur"), () => Shell.Reveal(Path)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menus.Item(L("Hang back on the line", "Volver a colgar en el tendedero", "Raccrocher au fil"), () => board.HangBack(this),
                                      tip: L("Or drag it to the top of the screen", "O arrástrala a la parte de arriba de la pantalla", "Ou faites-la glisser en haut de l’écran")));
            if (inInbox)
                menu.Items.Add(Menus.Item(L("Save to Screenshots", "Guardar en Capturas", "Enregistrer dans Captures d’écran"), Keep));
            menu.Items.Add(new Separator());
            if (inInbox)
            {
                menu.Items.Add(Menus.Item(L("Discard", "Descartar", "Jeter"), Discard));
            }
            else
            {
                menu.Items.Add(Menus.Item(L("Unpin", "Desfijar", "Détacher"), Unpin));
                menu.Items.Add(Menus.Item(L("Move to Recycle Bin", "Mover a la Papelera de reciclaje", "Placer dans la Corbeille"), Recycle));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Menus.Item(L("Scroll to resize, Ctrl+scroll to fade", "Rueda para cambiar el tamaño, Ctrl+rueda para atenuar", "Molette pour redimensionner, Ctrl+molette pour estomper"),
                                      () => { }, enabled: false));
            Menus.Show(menu);
        }

        void Copy()
        {
            badgeT.Tween(1, 0.15, Ease.Out, () => Delay.Run(1, () => badgeT.Tween(0, 0.3, Ease.Out)));
            FileActions.CopyInBackground(Path);
        }

        void Keep()
        {
            var kept = board.Line.Keep?.Invoke(Path);
            if (kept == null) return;
            Path = kept;
            lastWrite = LastWriteOf(kept);
            board.Save();
        }

        /// <summary>The cross: captures in the inbox go to the Recycle Bin, anything else is simply unpinned.</summary>
        void Discard()
        {
            if (Inbox.Contains(Path)) Recycle();
            else Unpin();
        }

        /// <summary>Goes to the Recycle Bin once the Undo on offer runs out.</summary>
        void Recycle()
        {
            string entry = Describe(), path = Path;
            if (board.Line.SoundOn) Sounds.Recycle();
            Leave();
            Undo.Offer(L("Moved to the Recycle Bin", "Movida a la Papelera de reciclaje", "Placée dans la Corbeille"),
                       () => board.Repin(entry), () => FileActions.Recycle(path));
        }

        void Unpin()
        {
            string entry = Describe();
            if (board.Line.SoundOn) Sounds.Pop();
            Leave();
            Undo.Offer(L("Unpinned", "Desfijada", "Détachée"), () => board.Repin(entry));
        }

        /// <summary>It lets go of the screen: a short shrink and fade, then gone.</summary>
        void Leave()
        {
            moveTimer?.Stop();
            var out_ = new Motion(1, v => Opacity = v, 0.002);
            out_.Tween(0, 0.18, Ease.In, () =>
            {
                Close();
                board.Removed(this);
            });
        }

        /// <summary>Once a second: gone files unpin, edited ones refresh, and pins step aside for full screen apps.</summary>
        public void Check()
        {
            if (moving) return;
            if (!File.Exists(Path))
            {
                Close();
                board.Removed(this);
                return;
            }
            var written = LastWriteOf(Path);
            if (written != lastWrite && Imaging.IsReady(Path))
            {
                var image = Imaging.LoadThumbnail(Path, 1600, out int w, out int h);
                if (image != null)
                {
                    lastWrite = written;
                    picture.Source = image;
                    if (w * imageHeight != h * imageWidth)
                        Place(new Rect(photo.X, photo.Y, photo.Width, photo.Width * h / w));
                    imageWidth = w;
                    imageHeight = h;
                }
            }
            var center = new POINT((int)(photo.X + photo.Width / 2), (int)(photo.Y + photo.Height / 2));
            bool full = FullScreen.IsActive(Monitors.At(center));
            if (full != hidden)
            {
                hidden = full;
                if (full) Hide();
                else
                {
                    Show();
                    ApplyBounds();
                }
            }
        }

        static DateTime LastWriteOf(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }
    }
}

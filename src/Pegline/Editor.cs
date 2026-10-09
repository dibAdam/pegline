using System;
using System.Collections.Generic;
using File = System.IO.File;
using MemoryStream = System.IO.MemoryStream;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static Pegline.Loc;
using IOPath = System.IO.Path;

namespace Pegline
{
    /// <summary>
    /// A small markup editor, the Windows counterpart of the one macOS opens
    /// from a screenshot thumbnail: crop, draw, point, box, highlight, blur
    /// away what should not be seen, and type. Saving writes over the file,
    /// and the photo on the line shows the new version.
    /// </summary>
    sealed class MarkupWindow : Window
    {
        enum Tool { Crop, Pen, Arrow, Box, Highlight, Blur, Text }

        static readonly Color[] Inks =
        {
            Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0xFF, 0xD6, 0x0A),
            Color.FromRgb(0x2F, 0xB3, 0x6A), Color.FromRgb(0x3E, 0x8B, 0xFF), Color.FromRgb(0x1C, 0x1C, 0x1E), Colors.White
        };
        static readonly double[] Sizes = { 0.7, 1.3, 2.4 };

        sealed class Step
        {
            public Action Undo, Redo;
        }

        readonly string path;
        readonly Action<string> saved;
        readonly BitmapSource original;
        readonly int pw, ph;
        readonly double unit;
        readonly Grid surface = new Grid();
        readonly Image baseImage = new Image { Stretch = Stretch.Fill };
        readonly Canvas ink = new Canvas();
        readonly Canvas overlay = new Canvas { IsHitTestVisible = false };
        readonly Path cropShade = new Path { Fill = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
        readonly Rectangle cropFrame = new Rectangle { Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 4, 3 } };
        readonly Rectangle marquee = new Rectangle { Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 3, 3 }, Visibility = Visibility.Collapsed };
        readonly Dictionary<Tool, ToggleButton> toolButtons = new Dictionary<Tool, ToggleButton>();
        readonly List<ToggleButton> inkButtons = new List<ToggleButton>(), sizeButtons = new List<ToggleButton>();
        readonly List<Step> undo = new List<Step>(), redo = new List<Step>();
        Button undoButton, redoButton;

        Tool tool = Tool.Arrow;
        Color color = Inks[0];
        int size = 1;
        Rect? crop;
        bool dirty;

        // A stroke or shape being drawn.
        Point start;
        bool drawing;
        Polyline pen;
        Shape shape;
        TextBox typing;

        /// <param name="saved">Called with the file once the edited image is written back.</param>
        public static void Open(string path, Action<string> saved)
        {
            var image = Imaging.LoadFull(path);
            if (image == null)
            {
                Shell.Edit(path);
                return;
            }
            var window = new MarkupWindow(path, image, saved);
            window.Show();
            window.Activate();
        }

        /// <summary>Development: draws one of each, crops and saves, to check the whole path without a mouse.</summary>
        public static void Exercise(string path)
        {
            var image = Imaging.LoadFull(path);
            if (image == null) return;
            var w = new MarkupWindow(path, image, null);
            w.Show();
            Delay.Run(0.6, () =>
            {
                var a = new Point(w.pw * 0.2, w.ph * 0.3);
                var b = new Point(w.pw * 0.55, w.ph * 0.7);
                var box = new Rectangle { Stroke = new SolidColorBrush(Inks[0]), StrokeThickness = w.StrokeWidth, RadiusX = w.StrokeWidth, RadiusY = w.StrokeWidth };
                Place(box, new Rect(a, b));
                w.ink.Children.Add(box);
                w.Added(box);
                var arrow = new Path { Stroke = new SolidColorBrush(Inks[4]), Fill = new SolidColorBrush(Inks[4]), StrokeThickness = w.StrokeWidth, Data = Arrow(new Point(w.pw * 0.9, w.ph * 0.9), b, w.StrokeWidth) };
                w.ink.Children.Add(arrow);
                w.Added(arrow);
                w.Added(w.Pixelate(new Rect(w.pw * 0.62, w.ph * 0.12, w.pw * 0.26, w.ph * 0.22)));
                w.SetCrop(new Rect(w.pw * 0.1, w.ph * 0.1, w.pw * 0.8, w.ph * 0.8), record: true);
                Delay.Run(1.5, () =>
                {
                    bool ok = w.SaveImage();
                    Log.Info($"Editor exercise saved: {ok}");
                    w.Close();
                });
            });
        }

        MarkupWindow(string path, BitmapSource image, Action<string> saved)
        {
            this.path = path;
            this.saved = saved;
            original = image;
            pw = image.PixelWidth;
            ph = image.PixelHeight;
            // Lines and text scale with the screenshot, so they read the same on a small snip and a 4K capture.
            unit = Math.Max(2.5, Math.Sqrt(pw * (double)pw + ph * (double)ph) / 450);

            Title = L("Edit", "Editar", "Modifier") + " — " + IOPath.GetFileName(path);
            Icon = AppIcon.Load(32);
            FontFamily = Visuals.UiFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "Pl.DialogFooter");
            var monitor = Monitors.UnderCursor() ?? Monitors.Primary;
            double s = monitor.Scale;
            double maxW = monitor.WorkArea.Width / s * 0.86, maxH = monitor.WorkArea.Height / s * 0.86;
            Width = Math.Max(760, Math.Min(maxW, pw / s + 80));
            Height = Math.Max(520, Math.Min(maxH, ph / s + 160));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SourceInitialized += (sender, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                int dark = Theme.AppsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            };

            baseImage.Source = image;
            RenderOptions.SetBitmapScalingMode(baseImage, BitmapScalingMode.HighQuality);
            surface.Width = pw;
            surface.Height = ph;
            surface.Background = Brushes.Transparent;
            surface.ClipToBounds = true;
            surface.Children.Add(baseImage);
            surface.Children.Add(ink);
            surface.Children.Add(overlay);
            cropFrame.StrokeThickness = Math.Max(1.5, unit / 2);
            marquee.StrokeThickness = Math.Max(1.5, unit / 2);
            overlay.Children.Add(cropShade);
            overlay.Children.Add(cropFrame);
            overlay.Children.Add(marquee);
            UpdateCrop();

            surface.MouseLeftButtonDown += OnDown;
            surface.MouseMove += OnMove;
            surface.MouseLeftButtonUp += OnUp;

            var stage = new Grid { Margin = new Thickness(24), Children = { new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = surface } } };
            var root = new DockPanel();
            var bar = Toolbar();
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(stage);
            Content = root;

            PreviewKeyDown += OnKey;
            Closing += OnClosing;
            Choose(tool);
            UpdateUndo();
        }

        // MARK: Toolbar

        FrameworkElement Toolbar()
        {
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            AddTool(left, Tool.Crop, "", L("Crop", "Recortar", "Rogner"), "C");
            AddTool(left, Tool.Pen, "", L("Pen", "Lápiz", "Stylo"), "P");
            AddTool(left, Tool.Arrow, "", L("Arrow", "Flecha", "Flèche"), "A", rotate: -45);
            AddTool(left, Tool.Box, "", L("Box", "Rectángulo", "Cadre"), "R");
            AddTool(left, Tool.Highlight, "", L("Highlight", "Resaltar", "Surligner"), "H");
            AddTool(left, Tool.Blur, "", L("Blur, to hide what should not be seen", "Difuminar, para ocultar lo que no debe verse", "Flouter, pour masquer ce qui ne doit pas se voir"), "B");
            AddTool(left, Tool.Text, "", L("Text", "Texto", "Texte"), "T");
            left.Children.Add(Divider());
            for (int i = 0; i < Inks.Length; i++)
            {
                int index = i;
                var swatch = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Inks[i]), Stroke = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)), StrokeThickness = 1 };
                var b = new ToggleButton { Content = swatch, Style = (Style)FindResource("Pl.ToolToggle"), Width = 32 };
                b.Click += (sender, e) => { color = Inks[index]; UpdateChoices(); RestyleTyping(); };
                inkButtons.Add(b);
                left.Children.Add(b);
            }
            left.Children.Add(Divider());
            for (int i = 0; i < Sizes.Length; i++)
            {
                int index = i;
                double d = 4 + i * 4;
                var dot = new Ellipse { Width = d, Height = d };
                dot.SetResourceReference(Shape.FillProperty, "Pl.Text");
                var b = new ToggleButton
                {
                    Content = dot,
                    Style = (Style)FindResource("Pl.ToolToggle"),
                    Width = 32,
                    ToolTip = new[] { L("Thin", "Fino", "Fin"), L("Medium", "Medio", "Moyen"), L("Thick", "Grueso", "Épais") }[i]
                };
                b.Click += (sender, e) => { size = index; UpdateChoices(); RestyleTyping(); };
                sizeButtons.Add(b);
                left.Children.Add(b);
            }
            left.Children.Add(Divider());
            undoButton = ToolButton("", L("Undo (Ctrl+Z)", "Deshacer (Ctrl+Z)", "Annuler (Ctrl+Z)"), DoUndo);
            redoButton = ToolButton("", L("Redo (Ctrl+Y)", "Rehacer (Ctrl+Y)", "Rétablir (Ctrl+Y)"), DoRedo);
            left.Children.Add(undoButton);
            left.Children.Add(redoButton);

            var copy = new Button { Content = L("Copy", "Copiar", "Copier"), Style = (Style)FindResource("Pl.Button"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Ctrl+C" };
            copy.Click += (sender, e) => CopyImage();
            var done = new Button { Content = L("Save", "Guardar", "Enregistrer"), Style = (Style)FindResource("Pl.AccentButton"), MinWidth = 88, ToolTip = "Ctrl+S" };
            done.Click += (sender, e) => { if (SaveImage()) Close(); };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { copy, done } };

            var dock = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
            dock.Children.Add(left);
            var bar = new Border { Padding = new Thickness(12, 8, 14, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = dock };
            bar.SetResourceReference(Border.BackgroundProperty, "Pl.DialogBackground");
            bar.SetResourceReference(Border.BorderBrushProperty, "Pl.DialogDivider");
            return bar;
        }

        void AddTool(Panel panel, Tool which, string glyph, string name, string key, double rotate = 0)
        {
            var text = new TextBlock { Text = glyph, RenderTransformOrigin = new Point(0.5, 0.5) };
            if (rotate != 0) text.RenderTransform = new RotateTransform(rotate);
            var b = new ToggleButton { Content = text, Style = (Style)FindResource("Pl.ToolToggle"), ToolTip = $"{name} ({key})" };
            b.Click += (sender, e) => Choose(which);
            toolButtons[which] = b;
            panel.Children.Add(b);
        }

        Button ToolButton(string glyph, string tip, Action click)
        {
            var b = new Button { Content = glyph, Style = (Style)FindResource("Pl.Tool"), ToolTip = tip };
            b.Click += (sender, e) => click();
            return b;
        }

        static FrameworkElement Divider()
        {
            var line = new Border { Width = 1, Height = 22, Margin = new Thickness(8, 0, 8, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "Pl.Separator");
            return line;
        }

        void Choose(Tool which)
        {
            CommitText();
            tool = which;
            // Highlighting is yellow unless another color was picked on purpose.
            if (which == Tool.Highlight && color == Inks[0]) color = Inks[2];
            surface.Cursor = which == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateChoices();
        }

        void UpdateChoices()
        {
            foreach (var pair in toolButtons) pair.Value.IsChecked = pair.Key == tool;
            for (int i = 0; i < inkButtons.Count; i++) inkButtons[i].IsChecked = Inks[i] == color;
            for (int i = 0; i < sizeButtons.Count; i++) sizeButtons[i].IsChecked = i == size;
        }

        double StrokeWidth => unit * Sizes[size];

        // MARK: Drawing

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = Clamp(e.GetPosition(surface));
            if (typing != null)
            {
                CommitText();
                if (tool == Tool.Text) return;
            }
            if (tool == Tool.Text)
            {
                StartText(p);
                return;
            }
            start = p;
            drawing = true;
            surface.CaptureMouse();
            switch (tool)
            {
                case Tool.Pen:
                    pen = new Polyline
                    {
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = StrokeWidth,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Points = { p }
                    };
                    ink.Children.Add(pen);
                    break;
                case Tool.Arrow:
                    shape = new Path { Stroke = new SolidColorBrush(color), Fill = new SolidColorBrush(color), StrokeThickness = StrokeWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round };
                    ink.Children.Add(shape);
                    break;
                case Tool.Box:
                    shape = new Rectangle { Stroke = new SolidColorBrush(color), StrokeThickness = StrokeWidth, RadiusX = StrokeWidth, RadiusY = StrokeWidth };
                    ink.Children.Add(shape);
                    break;
                case Tool.Highlight:
                    shape = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B)), RadiusX = unit / 2, RadiusY = unit / 2 };
                    ink.Children.Add(shape);
                    break;
                case Tool.Blur:
                case Tool.Crop:
                    marquee.Visibility = Visibility.Visible;
                    Place(marquee, new Rect(p, p));
                    break;
            }
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!drawing) return;
            var p = Clamp(e.GetPosition(surface));
            switch (tool)
            {
                case Tool.Pen:
                    var last = pen.Points[pen.Points.Count - 1];
                    if ((p - last).Length > 1.5) pen.Points.Add(p);
                    break;
                case Tool.Arrow:
                    ((Path)shape).Data = Arrow(start, p, StrokeWidth);
                    break;
                case Tool.Box:
                case Tool.Highlight:
                    Place(shape, new Rect(start, p));
                    break;
                case Tool.Blur:
                case Tool.Crop:
                    Place(marquee, new Rect(start, p));
                    break;
            }
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!drawing) return;
            drawing = false;
            surface.ReleaseMouseCapture();
            var p = Clamp(e.GetPosition(surface));
            var rect = new Rect(start, p);
            bool tiny = rect.Width < 4 && rect.Height < 4;
            switch (tool)
            {
                case Tool.Pen:
                    if (pen.Points.Count == 1) pen.Points.Add(new Point(p.X + 0.1, p.Y + 0.1));
                    Added(pen);
                    pen = null;
                    break;
                case Tool.Arrow:
                case Tool.Box:
                case Tool.Highlight:
                    if (tiny) ink.Children.Remove(shape);
                    else Added(shape);
                    shape = null;
                    break;
                case Tool.Blur:
                    marquee.Visibility = Visibility.Collapsed;
                    if (!tiny) Added(Pixelate(rect));
                    break;
                case Tool.Crop:
                    marquee.Visibility = Visibility.Collapsed;
                    if (!tiny) SetCrop(rect, record: true);
                    break;
            }
        }

        Point Clamp(Point p) => new Point(Math.Max(0, Math.Min(pw, p.X)), Math.Max(0, Math.Min(ph, p.Y)));

        static void Place(FrameworkElement element, Rect r)
        {
            Canvas.SetLeft(element, r.X);
            Canvas.SetTop(element, r.Y);
            element.Width = r.Width;
            element.Height = r.Height;
        }

        static Geometry Arrow(Point from, Point to, double thickness)
        {
            var v = to - from;
            double length = v.Length;
            var group = new GeometryGroup();
            if (length < 1) return group;
            v /= length;
            double head = Math.Min(length * 0.6, Math.Max(thickness * 4.2, 12));
            var normal = new Vector(-v.Y, v.X);
            var neck = to - v * head * 0.85;
            group.Children.Add(new LineGeometry(from, neck));
            var tip = new PathFigure(to, new[]
            {
                new LineSegment(to - v * head + normal * head * 0.55, true),
                new LineSegment(to - v * head - normal * head * 0.55, true)
            }, true);
            group.Children.Add(new PathGeometry(new[] { tip }));
            return group;
        }

        /// <summary>Hides a region behind large square pixels of itself, so nothing in it can be read back.</summary>
        FrameworkElement Pixelate(Rect rect)
        {
            var r = new Int32Rect((int)rect.X, (int)rect.Y, Math.Max(1, (int)Math.Min(rect.Width, pw - (int)rect.X)), Math.Max(1, (int)Math.Min(rect.Height, ph - (int)rect.Y)));
            double block = Math.Max(8, unit * 3);
            var region = new CroppedBitmap(original, r);
            var small = new TransformedBitmap(region, new ScaleTransform(Math.Max(1.0 / r.Width, 1 / block), Math.Max(1.0 / r.Height, 1 / block)));
            small.Freeze();
            var image = new Image { Source = small, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Place(image, new Rect(r.X, r.Y, r.Width, r.Height));
            ink.Children.Add(image);
            return image;
        }

        // MARK: Text

        void StartText(Point p)
        {
            typing = new TextBox
            {
                MinWidth = 40,
                AcceptsReturn = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(Math.Max(1, unit / 3)),
                Padding = new Thickness(2),
                FontFamily = Visuals.UiFont,
                FontWeight = FontWeights.SemiBold
            };
            typing.SetResourceReference(Control.BorderBrushProperty, "Pl.Accent");
            RestyleTyping();
            Canvas.SetLeft(typing, p.X);
            Canvas.SetTop(typing, Math.Max(0, p.Y - typing.FontSize * 0.7));
            ink.Children.Add(typing);
            typing.Focus();
            Keyboard.Focus(typing);
        }

        void RestyleTyping()
        {
            if (typing == null) return;
            typing.FontSize = unit * 4.5 * Sizes[size];
            typing.Foreground = new SolidColorBrush(color);
            typing.CaretBrush = new SolidColorBrush(color);
        }

        void CommitText()
        {
            if (typing == null) return;
            var box = typing;
            typing = null;
            ink.Children.Remove(box);
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            bool light = color.R * 0.3 + color.G * 0.59 + color.B * 0.11 > 150;
            var text = new TextBlock
            {
                Text = box.Text,
                FontSize = box.FontSize,
                FontFamily = box.FontFamily,
                FontWeight = box.FontWeight,
                Foreground = box.Foreground,
                // A soft outline in the opposite tone, so it reads on any screenshot.
                Effect = new DropShadowEffect { Color = light ? Colors.Black : Colors.White, BlurRadius = unit * 1.6, ShadowDepth = 0, Opacity = light ? 0.55 : 0.75 }
            };
            Canvas.SetLeft(text, Canvas.GetLeft(box) + box.Padding.Left + box.BorderThickness.Left + 2);
            Canvas.SetTop(text, Canvas.GetTop(box) + box.Padding.Top + box.BorderThickness.Top);
            ink.Children.Add(text);
            Added(text);
        }

        // MARK: Crop

        void SetCrop(Rect? rect, bool record)
        {
            var before = crop;
            crop = rect;
            UpdateCrop();
            if (record) Record(() => { crop = before; UpdateCrop(); }, () => { crop = rect; UpdateCrop(); });
        }

        void UpdateCrop()
        {
            var full = new Rect(0, 0, pw, ph);
            if (crop is Rect c)
            {
                cropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(c));
                cropFrame.Visibility = Visibility.Visible;
                Place(cropFrame, c);
            }
            else
            {
                cropShade.Data = null;
                cropFrame.Visibility = Visibility.Collapsed;
            }
        }

        // MARK: Undo

        void Added(UIElement element)
        {
            Record(() => ink.Children.Remove(element), () => ink.Children.Add(element));
        }

        void Record(Action undoAction, Action redoAction)
        {
            undo.Add(new Step { Undo = undoAction, Redo = redoAction });
            redo.Clear();
            dirty = true;
            UpdateUndo();
        }

        void DoUndo()
        {
            CommitText();
            if (undo.Count == 0) return;
            var step = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            step.Undo();
            redo.Add(step);
            dirty = true;
            UpdateUndo();
        }

        void DoRedo()
        {
            if (redo.Count == 0) return;
            var step = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            step.Redo();
            undo.Add(step);
            dirty = true;
            UpdateUndo();
        }

        void UpdateUndo()
        {
            if (undoButton == null) return;
            undoButton.IsEnabled = undo.Count > 0;
            redoButton.IsEnabled = redo.Count > 0;
        }

        // MARK: Keys

        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (typing != null)
            {
                if (e.Key == Key.Escape || (e.Key == Key.Enter && !shift))
                {
                    CommitText();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && shift)
                {
                    typing.SelectedText = "\n";
                    typing.CaretIndex = typing.SelectionStart + 1;
                    typing.SelectionLength = 0;
                    e.Handled = true;
                }
                return;
            }
            if (ctrl && e.Key == Key.Z) { if (shift) DoRedo(); else DoUndo(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.Y) { DoRedo(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.S) { SaveImage(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.C) { CopyImage(); e.Handled = true; return; }
            if (ctrl) return;
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.C: Choose(Tool.Crop); break;
                case Key.P: Choose(Tool.Pen); break;
                case Key.A: Choose(Tool.Arrow); break;
                case Key.R: Choose(Tool.Box); break;
                case Key.H: Choose(Tool.Highlight); break;
                case Key.B: Choose(Tool.Blur); break;
                case Key.T: Choose(Tool.Text); break;
                case Key.D1: case Key.D2: case Key.D3:
                    size = e.Key - Key.D1;
                    UpdateChoices();
                    break;
                default: return;
            }
            e.Handled = true;
        }

        // MARK: Saving

        /// <summary>The picture as it will be saved: the screenshot, everything drawn on it, cropped.</summary>
        BitmapSource Render()
        {
            CommitText();
            overlay.Visibility = Visibility.Collapsed;
            // Exactly one screen pixel per image pixel, so nothing is resampled.
            RenderOptions.SetBitmapScalingMode(baseImage, BitmapScalingMode.NearestNeighbor);
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(surface)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, pw, ph)
                }, null, new Rect(0, 0, pw, ph));
            }
            bitmap.Render(visual);
            RenderOptions.SetBitmapScalingMode(baseImage, BitmapScalingMode.HighQuality);
            overlay.Visibility = Visibility.Visible;

            BitmapSource result = bitmap;
            if (crop is Rect c)
            {
                var r = new Int32Rect((int)Math.Round(c.X), (int)Math.Round(c.Y), (int)Math.Round(c.Width), (int)Math.Round(c.Height));
                r.Width = Math.Max(1, Math.Min(r.Width, pw - r.X));
                r.Height = Math.Max(1, Math.Min(r.Height, ph - r.Y));
                result = new CroppedBitmap(bitmap, r);
            }
            result.Freeze();
            return result;
        }

        void CopyImage()
        {
            try
            {
                var image = Render();
                var data = new DataObject();
                data.SetData("PNG", new MemoryStream(Imaging.Encode(image)), false);
                data.SetImage(image);
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception e)
            {
                Log.Error("Could not copy the edited image", e);
            }
        }

        /// <summary>Writes the edit over the file, in its own format. Returns false if it could not.</summary>
        bool SaveImage()
        {
            if (!dirty && crop == null) return true;
            try
            {
                var image = Render();
                string ext = IOPath.GetExtension(path).ToLowerInvariant();
                BitmapEncoder encoder;
                string target = path;
                switch (ext)
                {
                    case ".jpg":
                    case ".jpeg": encoder = new JpegBitmapEncoder { QualityLevel = 92 }; break;
                    case ".bmp": encoder = new BmpBitmapEncoder(); break;
                    case ".tif":
                    case ".tiff": encoder = new TiffBitmapEncoder(); break;
                    case ".png": encoder = new PngBitmapEncoder(); break;
                    default:
                        // A format Windows cannot write: keep the original and save a PNG beside it.
                        encoder = new PngBitmapEncoder();
                        target = Inbox.UniquePath(IOPath.GetDirectoryName(path), IOPath.GetFileNameWithoutExtension(path) + " (edited).png");
                        break;
                }
                encoder.Frames.Add(BitmapFrame.Create(image));
                string temp = target + ".pegline-saving";
                using (var stream = File.Create(temp)) encoder.Save(stream);
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                dirty = false;
                undo.Clear();
                redo.Clear();
                UpdateUndo();
                Log.Info("Saved an edit of " + IOPath.GetFileName(target));
                saved?.Invoke(target);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not save the edit", e);
                Prompt.Tell(L("Could not save", "No se pudo guardar", "Enregistrement impossible"), e.Message);
                return false;
            }
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CommitText();
            if (!dirty) return;
            bool save = Prompt.Ask(L("Save your changes?", "¿Guardar los cambios?", "Enregistrer les modifications ?"),
                                   L("Your markup will be written over the screenshot.", "Las marcas se guardarán sobre la captura.", "Vos annotations seront enregistrées sur la capture."),
                                   L("Save", "Guardar", "Enregistrer"), L("Discard", "Descartar", "Abandonner"));
            if (save && !SaveImage()) e.Cancel = true;
        }
    }
}

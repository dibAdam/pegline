using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>
    /// A large look at a photo, shown just below it after a moment's hover or
    /// when it is chosen with the keyboard. It never takes a click, and it
    /// spells out what each gesture does, so nobody has to guess.
    /// </summary>
    sealed class PreviewWindow : OverlayWindow
    {
        const double Gap = 18;

        readonly Image image = new Image { Stretch = Stretch.Fill };
        readonly TextBlock hints;
        readonly Border frame;
        readonly Motion fade;
        readonly TranslateTransform lift = new TranslateTransform();
        string loadedPath;
        DateTime loadedWrite;
        BitmapSource loaded;
        int loadedWidth, loadedHeight;

        public Guid? ShownId { get; private set; }

        public PreviewWindow() : base(clickThrough: true)
        {
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            hints = new TextBlock
            {
                FontFamily = Visuals.UiFont,
                FontSize = 11.5,
                Foreground = Palette.Secondary,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 8, 6, 2)
            };
            frame = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Margin = new Thickness(Gap),
                Background = Palette.Capsule,
                BorderBrush = Palette.Edge,
                BorderThickness = new Thickness(0.75),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = lift,
                Child = new StackPanel { Children = { image, hints } },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.32 }
            };
            Content = frame;
            fade = new Motion(0, v =>
            {
                Opacity = v;
                lift.Y = -6 * (1 - v);
            }, 0.002);
        }

        public void ShowFor(Pegged item, PxRect card, MonitorInfo monitor, bool keyboard)
        {
            if (!Load(item.Path)) return;
            double s = monitor.Scale;
            var work = monitor.WorkArea;
            double below = (work.Bottom - card.Bottom) / s;
            // Large, but never larger than the image itself, and always fitting below the line.
            double maxW = Math.Min(680, work.Width / s * 0.5), maxH = Math.Max(160, Math.Min(440, below - 110));
            double w = loadedWidth / s, h = loadedHeight / s;
            double fit = Math.Min(1, Math.Min(maxW / w, maxH / h));
            double dw = Math.Round(w * fit), dh = Math.Round(h * fit);
            image.Source = loaded;
            image.Width = dw;
            image.Height = dh;
            image.Clip = new RectangleGeometry(new Rect(0, 0, dw, dh), 9, 9);
            hints.MaxWidth = Math.Max(dw, 320);
            hints.Text = keyboard
                ? L("← → choose · Enter copy · E edit · P pin · Delete discard · Esc close",
                    "← → elegir · Intro copiar · E editar · P fijar · Supr descartar · Esc cerrar",
                    "← → choisir · Entrée copier · E modifier · P épingler · Suppr jeter · Échap fermer")
                : L("Click to copy · Hold to edit · Drag into an app · Shift+drag to pin · Scroll for older · Right-click for more",
                    "Clic para copiar · Mantén para editar · Arrastra a una app · Mayús+arrastrar para fijar · Rueda para ver más antiguas · Clic derecho para más",
                    "Clic pour copier · Maintenir pour modifier · Glisser vers une app · Maj+glisser pour épingler · Molette pour les plus anciennes · Clic droit pour plus");

            frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = frame.DesiredSize;
            int pw = (int)Math.Ceiling(size.Width * s), ph = (int)Math.Ceiling(size.Height * s);
            int left = card.Center.X - pw / 2;
            left = Math.Max(work.X, Math.Min(work.Right - pw, left));
            int top = card.Bottom + (int)(4 * s);
            top = Math.Min(top, work.Bottom - ph);
            SetPixelBounds(new PxRect(left, top, pw, ph));

            bool appearing = ShownId == null;
            ShownId = item.Id;
            if (appearing)
            {
                Opacity = 0;
                Show();
                ApplyBounds();
                fade.Tween(1, 0.14, Ease.Out);
            }
        }

        public void HideNow()
        {
            if (ShownId == null) return;
            ShownId = null;
            fade.Tween(0, 0.1, Ease.In, () => { if (ShownId == null) Hide(); });
        }

        readonly System.Collections.Generic.Dictionary<string, (DateTime written, BitmapSource image)> ready =
            new System.Collections.Generic.Dictionary<string, (DateTime, BitmapSource)>(StringComparer.OrdinalIgnoreCase);
        readonly System.Collections.Generic.HashSet<string> loading = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Decodes the large image on a worker thread while the pointer is still resting.</summary>
        public void Prefetch(string path)
        {
            DateTime written;
            try { written = System.IO.File.GetLastWriteTimeUtc(path); }
            catch { return; }
            if (ready.TryGetValue(path, out var hit) && hit.written == written) return;
            if (!loading.Add(path)) return;
            Pegline.Background.Run(() => Imaging.LoadThumbnail(path, 1400, out _, out _), image =>
            {
                loading.Remove(path);
                if (image == null) return;
                if (ready.Count > 8) ready.Clear();
                ready[path] = (written, image);
            });
        }

        bool Load(string path)
        {
            DateTime written;
            try { written = System.IO.File.GetLastWriteTimeUtc(path); }
            catch { return false; }
            if (path == loadedPath && written == loadedWrite && loaded != null) return true;
            BitmapSource image;
            if (ready.TryGetValue(path, out var hit) && hit.written == written) image = hit.image;
            else image = Imaging.LoadThumbnail(path, 1400, out _, out _);
            if (image == null) return false;
            loaded = image;
            loadedPath = path;
            loadedWrite = written;
            // The thumbnail's own size, in pixels, is what fits on screen.
            loadedWidth = image.PixelWidth;
            loadedHeight = image.PixelHeight;
            return true;
        }
    }
}

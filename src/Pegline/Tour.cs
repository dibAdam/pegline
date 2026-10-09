using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>
    /// Three short pages on first run, and from the menu at any time: how
    /// captures arrive, how to find the line, and what each gesture does.
    /// </summary>
    sealed class TourWindow : Window
    {
        static TourWindow open;

        readonly Grid body = new Grid();
        readonly StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Button back, next;
        int page;

        /// <param name="then">Runs once the tour is closed, however it was closed.</param>
        public static void ShowTour(Action then = null)
        {
            if (open != null)
            {
                open.Activate();
                return;
            }
            open = new TourWindow();
            open.Closed += (s, e) =>
            {
                open = null;
                then?.Invoke();
            };
            open.Show();
            open.Activate();
        }

        TourWindow()
        {
            Title = L("Welcome to Pegline", "Bienvenido a Pegline", "Bienvenue dans Pegline");
            Icon = AppIcon.Load(32);
            SizeToContent = SizeToContent.Height;
            Width = 520;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            FontFamily = Visuals.UiFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "Pl.DialogBackground");
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                int dark = Theme.AppsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            };

            back = new Button { Content = L("Back", "Atrás", "Retour"), Style = (Style)FindResource("Pl.Button"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            next = new Button { Content = "", Style = (Style)FindResource("Pl.AccentButton"), MinWidth = 96, IsDefault = true };
            back.Click += (s, e) => Go(page - 1);
            next.Click += (s, e) => { if (page == 2) Close(); else Go(page + 1); };
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
                if (e.Key == System.Windows.Input.Key.Right && page < 2) Go(page + 1);
                if (e.Key == System.Windows.Input.Key.Left && page > 0) Go(page - 1);
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { back, next } };
            var footerGrid = new Grid();
            footerGrid.Children.Add(dots);
            footerGrid.Children.Add(buttons);
            var footer = new Border { Padding = new Thickness(24, 16, 24, 16), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerGrid };
            footer.SetResourceReference(Border.BackgroundProperty, "Pl.DialogFooter");
            footer.SetResourceReference(Border.BorderBrushProperty, "Pl.DialogDivider");

            var root = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            body.Margin = new Thickness(32, 28, 32, 26);
            body.MinHeight = 270;
            root.Children.Add(body);
            Content = root;
            Go(0);
        }

        void Go(int to)
        {
            page = Math.Max(0, Math.Min(2, to));
            body.Children.Clear();
            switch (page)
            {
                case 0:
                    Page("",
                         L("Take a screenshot", "Haz una captura", "Faites une capture"),
                         L("Use Win + Shift + S, Print Screen or any capture tool you like. Each capture flies onto a line at the top of your screen.",
                           "Usa Win + Mayús + S, Impr Pant o la herramienta que prefieras. Cada captura vuela a un tendedero en la parte de arriba de la pantalla.",
                           "Utilisez Win + Maj + S, Impr. écran ou l’outil de votre choix. Chaque capture s’envole vers un fil en haut de l’écran."), null);
                    break;
                case 1:
                    Page("",
                         L("Find your line", "Encuentra tu tendedero", "Retrouvez votre fil"),
                         L("Rest the pointer against the very top of the screen, click the little tab up there, or press the shortcut. Move away and it tucks itself up again.",
                           "Deja el puntero contra el borde superior de la pantalla, haz clic en la pequeña pestaña de arriba o pulsa el atajo. Al alejarte, vuelve a esconderse.",
                           "Laissez le pointeur contre le bord supérieur de l’écran, cliquez sur le petit onglet en haut ou appuyez sur le raccourci. Éloignez-vous et il se range."),
                         Shortcuts(new[]
                         {
                             (Shortcut.Load().ToString(), L("Show or hide the line", "Mostrar u ocultar el tendedero", "Afficher ou masquer le fil")),
                             (L("Tray icon", "Icono de la bandeja", "Icône de la barre"), L("Click to show, right-click for settings", "Clic para mostrar, clic derecho para configurar", "Clic pour afficher, clic droit pour les paramètres")),
                         }));
                    break;
                default:
                    Page("",
                         L("Use a screenshot", "Usa una captura", "Utilisez une capture"),
                         null,
                         Shortcuts(new[]
                         {
                             (L("Click", "Clic", "Clic"), L("Copy it", "Copiarla", "La copier")),
                             (L("Hold", "Mantener", "Maintenir"), L("Mark it up: crop, arrows, blur", "Marcarla: recortar, flechas, desenfocar", "L’annoter : rogner, flèches, flouter")),
                             (L("Drag", "Arrastrar", "Glisser"), L("Into an app to share, into a folder to keep", "A una app para compartir, a una carpeta para guardar", "Vers une app pour partager, vers un dossier pour garder")),
                             (L("Shift + drag", "Mayús + arrastrar", "Maj + glisser"), L("Pin it to the screen", "Fijarla en la pantalla", "L’épingler à l’écran")),
                             (L("Scroll", "Rueda", "Molette"), L("Bring back older captures", "Recuperar capturas anteriores", "Retrouver les captures plus anciennes")),
                             (L("Right-click", "Clic derecho", "Clic droit"), L("Everything else", "Todo lo demás", "Tout le reste")),
                         }));
                    break;
            }
            back.Visibility = page == 0 ? Visibility.Hidden : Visibility.Visible;
            next.Content = page == 2 ? L("Get started", "Empezar", "Commencer") : L("Next", "Siguiente", "Suivant");
            dots.Children.Clear();
            for (int i = 0; i < 3; i++)
            {
                var dot = new Ellipse { Width = i == page ? 18 : 6, Height = 6, Margin = new Thickness(0, 0, 6, 0) };
                if (i == page)
                {
                    dot = null;
                    var pill = new Border { Width = 18, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) };
                    pill.SetResourceReference(Border.BackgroundProperty, "Pl.Accent");
                    dots.Children.Add(pill);
                }
                else
                {
                    dot.SetResourceReference(Shape.FillProperty, "Pl.TextDisabled");
                    dots.Children.Add(dot);
                }
            }
        }

        void Page(string glyph, string title, string text, FrameworkElement extra)
        {
            var icon = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(28),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 18),
                Child = new TextBlock { Text = glyph, FontFamily = Visuals.IconFont, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            icon.SetResourceReference(Border.BackgroundProperty, "Pl.Accent");
            ((TextBlock)icon.Child).SetResourceReference(TextBlock.ForegroundProperty, "Pl.AccentText");

            var stack = new StackPanel();
            stack.Children.Add(icon);
            var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Pl.DisplayFont"), Margin = new Thickness(0, 0, 0, 10) };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "Pl.Text");
            stack.Children.Add(heading);
            if (text != null)
            {
                var body = new TextBlock { Text = text, FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
                body.SetResourceReference(TextBlock.ForegroundProperty, "Pl.TextSecondary");
                stack.Children.Add(body);
            }
            if (extra != null) stack.Children.Add(extra);
            this.body.Children.Add(stack);
        }

        FrameworkElement Shortcuts((string key, string meaning)[] rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 4),
                    Margin = new Thickness(0, 0, 14, 8),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = rows[i].key, FontSize = 12.5, FontWeight = FontWeights.SemiBold }
                };
                key.SetResourceReference(Border.BackgroundProperty, "Pl.ButtonBackground");
                key.SetResourceReference(Border.BorderBrushProperty, "Pl.ButtonBorder");
                ((TextBlock)key.Child).SetResourceReference(TextBlock.ForegroundProperty, "Pl.Text");
                var meaning = new TextBlock { Text = rows[i].meaning, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
                meaning.SetResourceReference(TextBlock.ForegroundProperty, "Pl.TextSecondary");
                Grid.SetRow(key, i);
                Grid.SetRow(meaning, i);
                Grid.SetColumn(meaning, 1);
                grid.Children.Add(key);
                grid.Children.Add(meaning);
            }
            return grid;
        }
    }
}

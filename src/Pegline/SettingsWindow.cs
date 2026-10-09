using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static Pegline.Loc;

namespace Pegline
{
    /// <summary>Every option in one place, laid out like Windows 11 Settings.</summary>
    sealed class SettingsWindow : Window
    {
        static SettingsWindow open;

        readonly Controller controller;
        readonly StackPanel page = new StackPanel { Margin = new Thickness(28, 20, 28, 28) };
        Button shortcutButton;
        TextBlock shortcutStatus, folderText;
        Button folderReset;
        bool recording;

        public static void ShowFor(Controller controller)
        {
            if (open != null)
            {
                open.Activate();
                return;
            }
            open = new SettingsWindow(controller);
            open.Closed += (s, e) => open = null;
            open.Show();
            open.Activate();
        }

        SettingsWindow(Controller controller)
        {
            this.controller = controller;
            Title = L("Pegline Settings", "Configuración de Pegline", "Paramètres de Pegline");
            Icon = AppIcon.Load(32);
            var work = (Monitors.UnderCursor() ?? Monitors.Primary).WorkArea;
            double scale = (Monitors.UnderCursor() ?? Monitors.Primary).Scale;
            Width = 640;
            Height = Math.Min(860, work.Height / scale - 60);
            MinWidth = 500;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Visuals.UiFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "Pl.DialogFooter");
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                int dark = Theme.AppsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            };
            PreviewKeyDown += OnKeyWhileRecording;

            Build();
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = page
            };
        }

        void Build()
        {
            page.Children.Add(Text(L("Settings", "Configuración", "Paramètres"), 28, FontWeights.SemiBold, display: true, bottom: 4));

            Section(L("The line", "El tendedero", "Le fil"));
            Row(L("Bring it down at the top edge", "Bajarlo desde el borde superior", "Le faire descendre depuis le bord supérieur"),
                L("Rest the pointer against the very top of the screen for a moment.",
                  "Deja el puntero un momento contra el borde superior de la pantalla.",
                  "Laissez le pointeur un instant contre le bord supérieur de l’écran."),
                Switch(!Settings.GetBool("EdgeRevealOff"), on => Settings.SetBool("EdgeRevealOff", !on)));
            Row(L("Show the pull tab", "Mostrar la pestaña", "Afficher l’onglet"),
                L("A small tab at the top while screenshots are waiting. Click it to open the line.",
                  "Una pequeña pestaña arriba mientras hay capturas esperando. Haz clic para abrir el tendedero.",
                  "Un petit onglet en haut tant que des captures attendent. Cliquez dessus pour ouvrir le fil."),
                Switch(!Settings.GetBool("PullTabOff"), on => { Settings.SetBool("PullTabOff", !on); controller.ApplySettings(); }));
            Row(L("Preview on hover", "Vista previa al pasar el puntero", "Aperçu au survol"),
                L("Hover a photo for a moment to see it large, with what each gesture does.",
                  "Pasa el puntero sobre una foto un momento para verla en grande, con lo que hace cada gesto.",
                  "Survolez une photo un instant pour la voir en grand, avec ce que fait chaque geste."),
                Switch(!Settings.GetBool("PreviewOff"), on => Settings.SetBool("PreviewOff", !on)));
            Row(L("Photos on the line", "Fotos en el tendedero", "Photos sur le fil"),
                L("How many hang before the oldest falls off the end. Scroll over the photos to bring older ones back.",
                  "Cuántas cuelgan antes de que la más antigua caiga por el extremo. Usa la rueda sobre las fotos para recuperar las anteriores.",
                  "Combien pendent avant que la plus ancienne tombe au bout. Faites défiler sur les photos pour retrouver les plus anciennes."),
                Stepper());

            Section(L("Shortcut", "Atajo", "Raccourci"));
            shortcutButton = new Button { Style = (Style)FindResource("Pl.Button"), MinWidth = 150 };
            shortcutButton.Click += (s, e) => StartRecording();
            shortcutStatus = Text("", 12, FontWeights.Normal, secondary: true);
            shortcutStatus.Margin = new Thickness(0, 6, 0, 0);
            shortcutStatus.Visibility = Visibility.Collapsed;
            var shortcutBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { shortcutButton, shortcutStatus } };
            Row(L("Show or hide the line", "Mostrar u ocultar el tendedero", "Afficher ou masquer le fil"),
                L("Click the button, then press the keys you want. Backspace removes the shortcut.",
                  "Haz clic en el botón y pulsa las teclas que quieras. Retroceso quita el atajo.",
                  "Cliquez sur le bouton, puis appuyez sur les touches voulues. Retour arrière supprime le raccourci."),
                shortcutBox);
            ShowShortcut();

            Section(L("Screenshots", "Capturas", "Captures"));
            Row(L("Handle my screenshots", "Encargarse de mis capturas", "S’occuper de mes captures"),
                L("New captures move into Pegline's own folder, and snips you only copy are caught too. Only what you keep goes back to your Screenshots folder.",
                  "Las capturas nuevas pasan a la carpeta de Pegline, y también se recogen los recortes que solo copias. Solo lo que guardas vuelve a tu carpeta de Capturas.",
                  "Les nouvelles captures vont dans le dossier de Pegline, et celles simplement copiées sont récupérées aussi. Seules celles que vous gardez retournent dans Captures d’écran."),
                Switch(Inbox.IsEnabled, on => controller.SetInbox(on)));
            folderText = Text(controller.WatchFolder, 12, FontWeights.Normal, secondary: true);
            folderText.TextTrimming = TextTrimming.CharacterEllipsis;
            var change = SmallButton(L("Change…", "Cambiar…", "Modifier…"), () =>
            {
                controller.ChooseFolder();
                UpdateFolder();
            });
            folderReset = SmallButton(L("Use Windows' folder", "Usar la de Windows", "Utiliser celui de Windows"), () =>
            {
                controller.ResetFolder();
                UpdateFolder();
            });
            var openFolder = SmallButton(L("Open", "Abrir", "Ouvrir"), controller.OpenScreenshotsFolder);
            Row(L("Watched folder", "Carpeta vigilada", "Dossier surveillé"), null,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { openFolder, change, folderReset } }, folderText);
            UpdateFolder();

            Section(L("General", "General", "Général"));
            Row(L("Sounds", "Sonidos", "Sons"),
                L("A tick when a capture hangs, a pop when one comes down.", "Un tic al colgar una captura, un pop al descolgarla.", "Un tic quand une capture s’accroche, un pop quand elle tombe."),
                Switch(!Settings.GetBool("SoundOff"), on => Settings.SetBool("SoundOff", !on)));
            Row(L("Lights after sunset", "Luces al anochecer", "Guirlande à la tombée de la nuit"),
                L("The line becomes a string of fairy lights in the evening.", "Por la noche el tendedero se convierte en una guirnalda de luces.", "Le soir, le fil devient une guirlande lumineuse."),
                Switch(!Settings.GetBool("NightLightsOff"), on => { Settings.SetBool("NightLightsOff", !on); controller.ApplySettings(); }));
            Row(L("Open at login", "Abrir al iniciar sesión", "Ouvrir à l’ouverture de session"),
                L("Start Pegline when you sign in to Windows.", "Iniciar Pegline al entrar en Windows.", "Démarrer Pegline à l’ouverture de session Windows."),
                Switch(LaunchAtLogin.IsEnabled, on =>
                {
                    try { LaunchAtLogin.Set(on); }
                    catch (Exception e) { Log.Error("Could not change the login setting", e); }
                }));
            Row(L("Show me around", "Enséñame cómo funciona", "Faire le tour"),
                L("A quick tour of what Pegline can do.", "Un recorrido rápido por lo que puede hacer Pegline.", "Un tour rapide de ce que Pegline sait faire."),
                SmallButton(L("Start tour", "Empezar", "Commencer"), () => TourWindow.ShowTour()));

            var version = typeof(SettingsWindow).Assembly.GetName().Version;
            var about = Text($"Pegline {version.Major}.{version.Minor}.{version.Build} · " +
                             L("A Windows port of Tendedero by Alejandro Buján.", "Una versión para Windows de Tendedero, de Alejandro Buján.", "Une adaptation pour Windows de Tendedero, d’Alejandro Buján."),
                             12, FontWeights.Normal, secondary: true);
            about.Margin = new Thickness(2, 24, 0, 0);
            page.Children.Add(about);
        }

        // MARK: Building blocks

        TextBlock Text(string text, double size, FontWeight weight, bool secondary = false, bool display = false, double bottom = 0)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
            if (display) t.FontFamily = (FontFamily)FindResource("Pl.DisplayFont");
            t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "Pl.TextSecondary" : "Pl.Text");
            return t;
        }

        void Section(string title)
        {
            var t = Text(title, 14, FontWeights.SemiBold);
            t.Margin = new Thickness(2, 22, 0, 8);
            page.Children.Add(t);
        }

        void Row(string title, string description, FrameworkElement control, FrameworkElement extra = null)
        {
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(Text(title, 14, FontWeights.Normal));
            if (description != null)
            {
                var d = Text(description, 12, FontWeights.Normal, secondary: true);
                d.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(d);
            }
            if (extra != null)
            {
                extra.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(extra);
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(left);
            grid.Children.Add(control);
            var card = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 3),
                BorderThickness = new Thickness(1),
                Child = grid
            };
            card.SetResourceReference(Border.BackgroundProperty, "Pl.DialogBackground");
            card.SetResourceReference(Border.BorderBrushProperty, "Pl.DialogDivider");
            page.Children.Add(card);
        }

        ToggleButton Switch(bool on, Action<bool> changed)
        {
            var toggle = new ToggleButton { IsChecked = on, Style = (Style)FindResource("Pl.Switch") };
            toggle.Checked += (s, e) => changed(true);
            toggle.Unchecked += (s, e) => changed(false);
            return toggle;
        }

        Button SmallButton(string text, Action click)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("Pl.Button"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>Auto, or a fixed number from 3 to 12.</summary>
        FrameworkElement Stepper()
        {
            var steps = new[] { 0, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            int index = Math.Max(0, Array.IndexOf(steps, Settings.GetInt("MaxCards")));
            var label = Text("", 14, FontWeights.SemiBold);
            label.MinWidth = 52;
            label.TextAlignment = TextAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            Action show = () => label.Text = steps[index] == 0 ? L("Auto", "Auto", "Auto") : steps[index].ToString();
            Button minus = null, plus = null;
            Action apply = () =>
            {
                show();
                minus.IsEnabled = index > 0;
                plus.IsEnabled = index < steps.Length - 1;
                Settings.SetInt("MaxCards", steps[index]);
                controller.ApplySettings();
            };
            minus = new Button { Content = "", FontFamily = Visuals.IconFont, FontSize = 11, Width = 34, Style = (Style)FindResource("Pl.Button"), Padding = new Thickness(0) };
            plus = new Button { Content = "", FontFamily = Visuals.IconFont, FontSize = 11, Width = 34, Style = (Style)FindResource("Pl.Button"), Padding = new Thickness(0) };
            minus.Click += (s, e) => { if (index > 0) { index--; apply(); } };
            plus.Click += (s, e) => { if (index < steps.Length - 1) { index++; apply(); } };
            show();
            minus.IsEnabled = index > 0;
            plus.IsEnabled = index < steps.Length - 1;
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { minus, label, plus } };
        }

        void UpdateFolder()
        {
            folderText.Text = controller.WatchFolder;
            folderText.ToolTip = controller.WatchFolder;
            folderReset.Visibility = string.IsNullOrEmpty(Settings.GetString("WatchFolder")) ? Visibility.Collapsed : Visibility.Visible;
        }

        // MARK: Recording a shortcut

        void ShowShortcut()
        {
            shortcutButton.Content = controller.Shortcut.ToString();
            if (!controller.Shortcut.IsEmpty && !controller.ShortcutWorks)
                Status(L("Another app is using this shortcut. Choose a different one.",
                         "Otra aplicación usa este atajo. Elige otro.",
                         "Une autre application utilise ce raccourci. Choisissez-en un autre."), error: true);
        }

        void StartRecording()
        {
            recording = true;
            shortcutButton.Content = L("Press the keys…", "Pulsa las teclas…", "Appuyez sur les touches…");
            Status(L("Esc cancels.", "Esc cancela.", "Échap annule."), error: false);
        }

        void Status(string text, bool error)
        {
            shortcutStatus.Text = text;
            shortcutStatus.Visibility = Visibility.Visible;
            if (error) shortcutStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x5A, 0x4F));
            else shortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Pl.TextSecondary");
        }

        void OnKeyWhileRecording(object sender, KeyEventArgs e)
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                recording = false;
                shortcutStatus.Visibility = Visibility.Collapsed;
                ShowShortcut();
                return;
            }
            if (key == Key.Back || key == Key.Delete)
            {
                recording = false;
                controller.TrySetShortcut(new Shortcut(0, 0));
                shortcutStatus.Visibility = Visibility.Collapsed;
                ShowShortcut();
                return;
            }
            // Waiting for the key that goes with the modifiers.
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;

            var mods = Keyboard.Modifiers;
            uint flags = 0;
            if ((mods & ModifierKeys.Control) != 0) flags |= Native.MOD_CONTROL;
            if ((mods & ModifierKeys.Alt) != 0) flags |= Native.MOD_ALT;
            if ((mods & ModifierKeys.Shift) != 0) flags |= 4;
            if ((mods & ModifierKeys.Windows) != 0) flags |= 8;
            if ((flags & (Native.MOD_CONTROL | Native.MOD_ALT | 8)) == 0)
            {
                Status(L("Use Ctrl, Alt or Win with another key.", "Usa Ctrl, Alt o Win con otra tecla.", "Utilisez Ctrl, Alt ou Win avec une autre touche."), error: true);
                return;
            }
            recording = false;
            var shortcut = new Shortcut(flags, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (controller.TrySetShortcut(shortcut))
            {
                shortcutStatus.Visibility = Visibility.Collapsed;
                ShowShortcut();
            }
            else
            {
                ShowShortcut();
                Status(L($"{shortcut} is taken by another app. Try another.",
                         $"{shortcut} lo usa otra aplicación. Prueba otro.",
                         $"{shortcut} est déjà utilisé par une autre application. Essayez-en un autre."), error: true);
            }
        }
    }
}

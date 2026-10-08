using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pegline
{
    /// <summary>A small dialog in the style of a Windows 11 content dialog.</summary>
    sealed class Prompt : Window
    {
        bool accepted;

        public static bool Ask(string heading, string message, string primary, string secondary)
        {
            var prompt = new Prompt(heading, message, primary, secondary);
            prompt.ShowDialog();
            return prompt.accepted;
        }

        public static void Tell(string heading, string message) => Ask(heading, message, "OK", null);

        Prompt(string heading, string message, string primary, string secondary)
        {
            Title = "Pegline";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = true;
            Icon = AppIcon.Load(32);
            Background = (Brush)FindResource("Pl.DialogBackground");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = Visuals.UiFont;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var icon = new Image { Source = AppIcon.Load(128), Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 18, 0) };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var text = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = heading,
                        FontFamily = (FontFamily)FindResource("Pl.DisplayFont"),
                        FontSize = 20,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("Pl.Text"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10)
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 14,
                        LineHeight = 20,
                        Foreground = (Brush)FindResource("Pl.TextSecondary"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
            var body = new DockPanel { Margin = new Thickness(24, 24, 24, 24), LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
            body.Children.Add(text);

            var ok = new Button { Content = primary, IsDefault = true, Style = (Style)FindResource("Pl.AccentButton") };
            ok.Click += (s, e) => { accepted = true; Close(); };
            var buttons = new UniformGrid { Rows = 1, Columns = secondary == null ? 1 : 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            buttons.Children.Add(ok);
            if (secondary != null)
            {
                var cancel = new Button { Content = secondary, IsCancel = true, Style = (Style)FindResource("Pl.Button"), Margin = new Thickness(8, 0, 0, 0) };
                cancel.Click += (s, e) => Close();
                buttons.Children.Add(cancel);
            }
            var footer = new Border
            {
                Background = (Brush)FindResource("Pl.DialogFooter"),
                BorderBrush = (Brush)FindResource("Pl.DialogDivider"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24),
                Child = buttons
            };

            var root = new DockPanel { Width = 460 };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(body);
            Content = root;

            SourceInitialized += (s, e) =>
            {
                // A title bar that matches the theme.
                var handle = new WindowInteropHelper(this).Handle;
                int dark = Theme.AppsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            };
            Loaded += (s, e) =>
            {
                Activate();
                ok.Focus();
            };
        }
    }

    static class AppIcon
    {
        /// <summary>The frame of the app icon closest to the size asked for.</summary>
        public static BitmapSource Load(int size)
        {
            try
            {
                var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Pegline;component/Assets/Pegline.ico"),
                                                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First();
            }
            catch (Exception e)
            {
                Log.Error("Could not load the app icon", e);
                return null;
            }
        }
    }
}

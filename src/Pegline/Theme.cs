using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pegline
{
    /// <summary>
    /// Follows the Windows light and dark settings and the accent color. Apps
    /// and the taskbar have separate switches, so the tray icon reads one and
    /// everything else reads the other.
    /// </summary>
    static class Theme
    {
        public static bool AppsDark { get; private set; }
        public static bool TaskbarDark { get; private set; }
        public static Color Accent { get; private set; }

        public static event Action Changed;

        public static void Initialize(Application app)
        {
            Read();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Pegline;component/Theme.xaml", UriKind.Absolute)
            });
            ApplyResources(app.Resources);
            Palette.Apply();
            SystemEvents.UserPreferenceChanged += (s, e) =>
                app.Dispatcher.BeginInvoke(new Action(() => Refresh(app)));
        }

        static void Refresh(Application app)
        {
            bool apps = AppsDark, taskbar = TaskbarDark;
            var accent = Accent;
            Read();
            if (apps == AppsDark && taskbar == TaskbarDark && accent == Accent) return;
            ApplyResources(app.Resources);
            Palette.Apply();
            Changed?.Invoke();
        }

        static void Read()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    AppsDark = key?.GetValue("AppsUseLightTheme") is int apps && apps == 0;
                    TaskbarDark = !(key?.GetValue("SystemUsesLightTheme") is int system && system != 0);
                }
            }
            catch
            {
                AppsDark = false;
                TaskbarDark = true;
            }
            Accent = ReadAccent();
        }

        /// <summary>Windows 11 buttons use a lighter accent shade in dark mode and a deeper one in light mode.</summary>
        static Color ReadAccent()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
                    {
                        int i = (AppsDark ? 1 : 4) * 4;
                        return Color.FromRgb(palette[i], palette[i + 1], palette[i + 2]);
                    }
                }
            }
            catch { }
            return AppsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0);
        }

        static void ApplyResources(ResourceDictionary r)
        {
            bool d = AppsDark;
            r["Pl.Text"] = Brush(d ? 0xFFFFFFFF : 0xE4000000);
            r["Pl.TextSecondary"] = Brush(d ? 0xC5FFFFFF : 0x9E000000);
            r["Pl.TextDisabled"] = Brush(d ? 0x5DFFFFFF : 0x5C000000);
            r["Pl.MenuBackground"] = Brush(d ? 0xFF2C2C2C : 0xFFF9F9F9);
            r["Pl.MenuBorder"] = Brush(d ? 0xFF3D3D3D : 0xFFE3E3E3);
            r["Pl.MenuHover"] = Brush(d ? 0x14FFFFFF : 0x0A000000);
            r["Pl.Separator"] = Brush(d ? 0x19FFFFFF : 0x14000000);
            r["Pl.DialogBackground"] = Brush(d ? 0xFF2B2B2B : 0xFFFFFFFF);
            r["Pl.DialogFooter"] = Brush(d ? 0xFF202020 : 0xFFF3F3F3);
            r["Pl.DialogDivider"] = Brush(d ? 0xFF1D1D1D : 0xFFE5E5E5);
            r["Pl.ButtonBackground"] = Brush(d ? 0xFF373737 : 0xFFFEFEFE);
            r["Pl.ButtonBorder"] = Brush(d ? 0xFF444444 : 0xFFD6D6D6);
            r["Pl.Accent"] = new SolidColorBrush(Accent);
            r["Pl.AccentText"] = Brush(d ? 0xFF000000 : 0xFFFFFFFF);
        }

        static SolidColorBrush Brush(long argb)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Shared, live brushes for the line itself. Changing a color here repaints
    /// every card at once when the theme flips.
    /// </summary>
    static class Palette
    {
        /// <summary>The frosted frame around each photo. Windows cannot blur behind part of a window, so it is a milky tint instead.</summary>
        public static readonly SolidColorBrush Glass = new SolidColorBrush();
        /// <summary>The specular edge, lit from above.</summary>
        public static readonly LinearGradientBrush Edge = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 1) }
        };
        public static readonly SolidColorBrush Outline = new SolidColorBrush();
        public static readonly SolidColorBrush Primary = new SolidColorBrush();
        public static readonly SolidColorBrush Secondary = new SolidColorBrush();
        public static readonly SolidColorBrush Capsule = new SolidColorBrush();

        public static void Apply()
        {
            bool d = Theme.AppsDark;
            Glass.Color = d ? Color.FromArgb(0xC4, 0x30, 0x30, 0x34) : Color.FromArgb(0xC4, 0xF7, 0xF7, 0xF7);
            Edge.GradientStops[0].Color = Color.FromArgb(d ? (byte)0x55 : (byte)0x8C, 0xFF, 0xFF, 0xFF);
            Edge.GradientStops[1].Color = Color.FromArgb(d ? (byte)0x10 : (byte)0x1F, 0xFF, 0xFF, 0xFF);
            Outline.Color = Color.FromArgb(d ? (byte)0x66 : (byte)0x1A, 0, 0, 0);
            Primary.Color = d ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A);
            Secondary.Color = d ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA6, 0, 0, 0);
            Capsule.Color = d ? Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2E) : Color.FromArgb(0xE6, 0xF6, 0xF6, 0xF6);
        }
    }
}

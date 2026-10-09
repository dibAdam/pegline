using System;
using System.IO;
using Microsoft.Win32;

namespace Pegline
{
    /// <summary>
    /// Small preferences, kept in the registry under HKCU\Software\Pegline, the
    /// Windows counterpart of UserDefaults. A profile name keeps a development
    /// copy apart from the one you use.
    /// </summary>
    static class Settings
    {
        public static string Profile { get; private set; } = "Pegline";

        public static void UseProfile(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) Profile = name.Trim();
        }

        public static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Profile);

        static RegistryKey Open() => Registry.CurrentUser.CreateSubKey(@"Software\" + Profile);

        public static bool GetBool(string name, bool fallback = false)
        {
            try
            {
                using (var key = Open())
                    return key.GetValue(name) is int value ? value != 0 : fallback;
            }
            catch { return fallback; }
        }

        public static void SetBool(string name, bool value)
        {
            try { using (var key = Open()) key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord); }
            catch (Exception e) { Log.Error("Could not save " + name, e); }
        }

        public static int GetInt(string name, int fallback = 0)
        {
            try
            {
                using (var key = Open())
                    return key.GetValue(name) is int value ? value : fallback;
            }
            catch { return fallback; }
        }

        public static void SetInt(string name, int value)
        {
            try { using (var key = Open()) key.SetValue(name, value, RegistryValueKind.DWord); }
            catch (Exception e) { Log.Error("Could not save " + name, e); }
        }

        public static string GetString(string name)
        {
            try { using (var key = Open()) return key.GetValue(name) as string; }
            catch { return null; }
        }

        /// <summary>A null value removes the setting.</summary>
        public static void SetString(string name, string value)
        {
            try
            {
                using (var key = Open())
                {
                    if (value == null) key.DeleteValue(name, false);
                    else key.SetValue(name, value, RegistryValueKind.String);
                }
            }
            catch (Exception e) { Log.Error("Could not save " + name, e); }
        }

        public static string[] GetStrings(string name)
        {
            try { using (var key = Open()) return key.GetValue(name) as string[] ?? new string[0]; }
            catch { return new string[0]; }
        }

        public static void SetStrings(string name, string[] values)
        {
            try
            {
                using (var key = Open())
                {
                    if (values.Length == 0) key.DeleteValue(name, false);
                    else key.SetValue(name, values, RegistryValueKind.MultiString);
                }
            }
            catch (Exception e) { Log.Error("Could not save " + name, e); }
        }
    }
}

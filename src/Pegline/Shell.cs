using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pegline
{
    /// <summary>Hands files to the apps Windows already uses for them.</summary>
    static class Shell
    {
        public static void Open(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

        /// <summary>
        /// The Windows counterpart of Markup: the "Edit" verb, which opens Paint
        /// unless another editor took it over. Saving writes over the file, and
        /// the line picks up the new version.
        /// </summary>
        public static void Edit(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "edit" });
                return;
            }
            catch (Exception e)
            {
                Log.Info("No edit verb, trying Paint: " + e.Message);
            }
            try
            {
                Process.Start(new ProcessStartInfo("mspaint.exe", Quote(path)) { UseShellExecute = true });
            }
            catch
            {
                Open(path);
            }
        }

        public static void Reveal(string path) => Start(new ProcessStartInfo("explorer.exe", "/select," + Quote(path)));

        public static void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        static void Start(ProcessStartInfo info)
        {
            try { Process.Start(info); }
            catch (Exception e) { Log.Error("Could not open " + info.FileName, e); }
        }

        static string Quote(string s) => "\"" + s + "\"";
    }

    /// <summary>Open at login, through the per-user Run key.</summary>
    static class LaunchAtLogin
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static string Name => Settings.Profile;
        static string Exe => Assembly.GetEntryAssembly().Location;

        static string Command
        {
            get
            {
                var command = $"\"{Exe}\" --startup";
                if (Settings.Profile != "Pegline") command += $" --profile \"{Settings.Profile}\"";
                return command;
            }
        }

        /// <summary>On, unless it was switched off in Task Manager's Startup apps.</summary>
        public static bool IsEnabled
        {
            get
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (!(run?.GetValue(Name) is string)) return false;
                }
                using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    return !(approved?.GetValue(Name) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
                }
            }
        }

        public static void Set(bool on)
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) run.SetValue(Name, Command);
                else run.DeleteValue(Name, false);
            }
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                approved?.DeleteValue(Name, false);
        }

        /// <summary>Points the entry at this copy, in case the app was moved since it was turned on.</summary>
        public static void Repair()
        {
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run?.GetValue(Name) is string current && current != Command) run.SetValue(Name, Command);
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not update the login item", e);
            }
        }
    }

    /// <summary>The modern Windows folder picker, which .NET Framework does not wrap.</summary>
    static class FolderPicker
    {
        public static string Pick(string title, string start)
        {
            IFileDialog dialog = null;
            try
            {
                dialog = (IFileDialog)new FileOpenDialogCom();
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | 0x20 /* PICKFOLDERS */ | 0x40 /* FORCEFILESYSTEM */);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)
                    && SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder) == 0)
                    dialog.SetFolder(folder);
                if (dialog.Show(IntPtr.Zero) != 0) return null;
                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out string path);
                return path;
            }
            catch (Exception e)
            {
                Log.Error("Folder picker failed", e);
                return null;
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}

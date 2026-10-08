using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Pegline
{
    /// <summary>
    /// Watches the folder Windows saves screenshots to and reports new ones.
    /// Pegline never takes screenshots itself: Win+Shift+S, Win+PrtScn, the
    /// Snipping Tool or ShareX keep working as always, and the line picks up
    /// what they save.
    /// </summary>
    sealed class ScreenshotWatcher : IDisposable
    {
        public string Folder { get; }

        readonly Action<string> onNew;
        readonly Action onChange;
        readonly Dispatcher dispatcher;
        readonly DispatcherTimer debounce;
        HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher fsw;

        /// <summary>
        /// Anything created after the watcher started counts as new, even if it
        /// landed while the watcher was still getting ready.
        /// </summary>
        readonly DateTime startedAt = DateTime.UtcNow;

        static readonly Guid ScreenshotsFolderId = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");

        public ScreenshotWatcher(string folder, Action<string> onNew, Action onChange)
        {
            Folder = folder;
            this.onNew = onNew;
            this.onChange = onChange;
            dispatcher = Application.Current.Dispatcher;
            // Writers often create the file, then fill it; give them a moment.
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            debounce.Tick += (s, e) => { debounce.Stop(); Scan(); };
        }

        /// <summary>
        /// The Windows Screenshots folder, wherever it really lives. OneDrive
        /// often moves it, and its name is translated, so it is asked for by id.
        /// A folder picked from the menu takes precedence.
        /// </summary>
        public static string ScreenshotsFolder()
        {
            var chosen = Settings.GetString("WatchFolder");
            if (!string.IsNullOrEmpty(chosen) && Directory.Exists(chosen)) return chosen;
            return SystemScreenshotsFolder();
        }

        public static string SystemScreenshotsFolder()
        {
            try
            {
                const uint KF_FLAG_DONT_VERIFY = 0x4000;
                if (Native.SHGetKnownFolderPath(ScreenshotsFolderId, KF_FLAG_DONT_VERIFY, IntPtr.Zero, out IntPtr p) == 0)
                {
                    var path = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not find the Screenshots folder", e);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        public void Start()
        {
            try
            {
                // Windows creates it on the first Win+PrtScn anyway.
                Directory.CreateDirectory(Folder);
            }
            catch (Exception e)
            {
                Log.Error("Cannot create " + Folder, e);
                return;
            }

            var files = Listing();
            known = new HashSet<string>(files.Where(f => f.CreationTimeUtc < startedAt).Select(f => f.FullName),
                                        StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                if (!known.Contains(file.FullName)) onNew(file.FullName);
            known = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);

            try
            {
                fsw = new FileSystemWatcher(Folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                FileSystemEventHandler changed = (s, e) => dispatcher.BeginInvoke(new Action(Schedule));
                fsw.Created += changed;
                fsw.Changed += changed;
                fsw.Deleted += changed;
                fsw.Renamed += (s, e) => dispatcher.BeginInvoke(new Action(Schedule));
                fsw.Error += (s, e) => dispatcher.BeginInvoke(new Action(() =>
                {
                    Log.Error("Folder watcher overflowed, rescanning", e.GetException());
                    Schedule();
                }));
                fsw.EnableRaisingEvents = true;
                Log.Info("Watching " + Folder);
            }
            catch (Exception e)
            {
                Log.Error("Cannot watch " + Folder, e);
            }
        }

        /// <summary>A file we are about to put here ourselves is not a new screenshot.</summary>
        public void Ignore(string path) => known.Add(path);

        void Schedule()
        {
            debounce.Stop();
            debounce.Start();
        }

        void Scan()
        {
            var files = Listing();
            foreach (var file in files)
                if (!known.Contains(file.FullName)) onNew(file.FullName);
            known = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);
            onChange();
        }

        /// <summary>
        /// Oldest first. Names, attributes and dates all come from the one
        /// directory read, so a folder with thousands of screenshots stays quick.
        /// </summary>
        List<FileInfo> Listing()
        {
            try
            {
                return new DirectoryInfo(Folder).EnumerateFiles()
                    .Where(f => (f.Attributes & FileAttributes.Hidden) == 0 && Imaging.IsImage(f.Name))
                    .OrderBy(f => f.CreationTimeUtc)
                    .ToList();
            }
            catch
            {
                return new List<FileInfo>();
            }
        }

        public void Dispose()
        {
            debounce.Stop();
            if (fsw != null)
            {
                fsw.EnableRaisingEvents = false;
                fsw.Dispose();
                fsw = null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic.FileIO;

namespace Pegline
{
    /// <summary>One screenshot hanging on the line.</summary>
    sealed class Pegged
    {
        static readonly Random random = new Random();

        public Guid Id { get; } = Guid.NewGuid();
        public string Path { get; }
        public BitmapSource Thumb { get; set; }
        public DateTime LastWrite { get; set; }
        /// <summary>When the capture was taken. Fresh ones are still wet; old ones curl.</summary>
        public DateTime Created { get; set; }
        /// <summary>Every photo hangs a little crooked, like on a real line.</summary>
        public double Tilt { get; } = random.NextDouble() * 5 - 2.5;
        public bool Falling { get; set; }
        /// <summary>Still flying in from where it was captured; the card waits hidden.</summary>
        public bool Flying { get; set; }

        public Pegged(string path, BitmapSource thumb)
        {
            Path = path;
            Thumb = thumb;
        }
    }

    /// <summary>
    /// The line itself: what hangs on it and what you can do with each item.
    /// The files never move unless you move them. The line is only a view onto them.
    /// It also remembers the captures that fell off the far end, so scrolling
    /// can bring them back.
    /// </summary>
    sealed class Line
    {
        static readonly Random random = new Random();
        const string StoreKey = "Pegged", ArchiveKey = "Archive";
        const int ArchiveLimit = 60;

        public List<Pegged> Items { get; } = new List<Pegged>();

        /// <summary>Captures that fell off the far end, newest first. Still on disk: scrolling brings them back.</summary>
        readonly List<string> archive = new List<string>();
        /// <summary>Newer captures that moved off the near end while looking back, oldest first.</summary>
        readonly List<string> ahead = new List<string>();

        public event Action ItemsChanged;
        public event Action<Pegged> ItemUpdated;
        public event Action Gust;
        public event Action StateChanged;
        public event Action RevealedChanged;

        /// <summary>Called just before a photo starts falling, so the fall can be drawn over the whole screen.</summary>
        public Action<Pegged> OnFall;

        /// <summary>Inbox mode: moves a kept capture to where screenshots live, and returns its new path.</summary>
        public Func<string, string> Keep;

        /// <summary>Opens the built-in markup editor on a file.</summary>
        public Action<string> OpenEditor;

        Guid? hovered, pressed, dragging, copied, selected;
        bool revealed;

        public Guid? HoveredId { get => hovered; set => SetState(ref hovered, value); }
        public Guid? PressedId { get => pressed; set => SetState(ref pressed, value); }
        public Guid? DraggingId { get => dragging; set => SetState(ref dragging, value); }
        public Guid? CopiedId { get => copied; set => SetState(ref copied, value); }
        /// <summary>The photo chosen with the keyboard.</summary>
        public Guid? SelectedId { get => selected; set => SetState(ref selected, value); }

        /// <summary>Whether the line has slid down into view.</summary>
        public bool Revealed
        {
            get => revealed;
            set
            {
                if (revealed == value) return;
                revealed = value;
                RevealedChanged?.Invoke();
            }
        }

        int maxItems = 8;

        /// <summary>How many photos hang at once. Lowering it moves the oldest into history, where scrolling finds them.</summary>
        public int MaxItems
        {
            get => maxItems;
            set
            {
                maxItems = Math.Max(1, value);
                var live = Live;
                if (live.Count <= maxItems) return;
                foreach (var oldest in live.Take(live.Count - maxItems))
                {
                    Forget(oldest.Id);
                    Items.Remove(oldest);
                    Remember(oldest.Path);
                }
                Save();
                ItemsChanged?.Invoke();
            }
        }

        public bool SoundOn
        {
            get => !Settings.GetBool("SoundOff");
            set => Settings.SetBool("SoundOff", !value);
        }

        public int LiveCount => Items.Count(i => !i.Falling);
        public List<Pegged> Live => Items.Where(i => !i.Falling).ToList();

        /// <summary>How many older captures scrolling can bring back.</summary>
        public int OlderCount => archive.Count;
        /// <summary>How many newer captures are out of view while looking back.</summary>
        public int NewerCount => ahead.Count;

        public Line()
        {
            Restore();
            ScheduleGust();
        }

        void SetState(ref Guid? field, Guid? value)
        {
            if (field == value) return;
            field = value;
            StateChanged?.Invoke();
        }

        public Pegged Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);

        // MARK: Hanging and dropping

        /// <summary>A new capture hangs at the near end. Looking back first returns to the newest.</summary>
        public Guid? Hang(string path, bool quietly = false, bool flying = false)
        {
            if (ahead.Count > 0) BackToNewest();
            return Add(path, Items.Count, quietly, flying, overflow: true);
        }

        Guid? Add(string path, int index, bool quietly, bool flying, bool overflow)
        {
            if (Items.Any(i => !i.Falling && SamePath(i.Path, path))) return null;
            var thumb = Imaging.LoadThumbnail(path, 480, out _, out _);
            if (thumb == null) return null;

            var item = new Pegged(path, thumb) { Flying = flying, LastWrite = LastWriteOf(path), Created = CreatedOf(path) };
            Items.Insert(Math.Max(0, Math.Min(index, Items.Count)), item);
            archive.RemoveAll(p => SamePath(p, path));
            ahead.RemoveAll(p => SamePath(p, path));
            // A full line lets the oldest photo fall off the far end; it is remembered.
            while (overflow && LiveCount > MaxItems)
            {
                var oldest = Items.FirstOrDefault(i => !i.Falling);
                if (oldest == null) break;
                Remember(oldest.Path);
                Drop(oldest.Id, quietly: true);
            }
            Save();
            ItemsChanged?.Invoke();
            if (!quietly && SoundOn) Sounds.Tink();
            Log.Info("Hung " + System.IO.Path.GetFileName(path));
            return item.Id;
        }

        void Remember(string path)
        {
            archive.RemoveAll(p => SamePath(p, path));
            archive.Insert(0, path);
            if (archive.Count > ArchiveLimit) archive.RemoveRange(ArchiveLimit, archive.Count - ArchiveLimit);
        }

        /// <summary>The capture has reached the line: the real card takes over.</summary>
        public void Land(Guid id)
        {
            var item = Find(id);
            if (item == null || !item.Flying) return;
            item.Flying = false;
            ItemUpdated?.Invoke(item);
        }

        public void Drop(Guid id, bool quietly = false)
        {
            var item = Find(id);
            if (item == null || item.Falling) return;
            OnFall?.Invoke(item);
            item.Falling = true;
            Forget(id);
            Save();
            ItemsChanged?.Invoke();
            if (!quietly && SoundOn) Sounds.Pop();
            Delay.Run(0.6, () =>
            {
                Items.Remove(item);
                ItemsChanged?.Invoke();
            });
        }

        void Forget(Guid id)
        {
            if (hovered == id) hovered = null;
            if (pressed == id) pressed = null;
            if (selected == id) selected = null;
        }

        /// <summary>Takes a photo off the line without letting it fall: it was pinned to the screen.</summary>
        public void Detach(Guid id)
        {
            var item = Find(id);
            if (item == null || item.Falling) return;
            Forget(id);
            Items.Remove(item);
            Save();
            ItemsChanged?.Invoke();
        }

        /// <summary>Takes everything down, with one Undo that puts it all back.</summary>
        public void Clear()
        {
            var live = Live;
            if (live.Count == 0) return;
            var paths = live.Select(i => i.Path).ToList();
            for (int n = 0; n < live.Count; n++)
            {
                var item = live[n];
                bool quietly = n > 0;
                Delay.Run(0.06 * n, () => Drop(item.Id, quietly));
            }
            Undo.Offer(Loc.L("Took everything down", "Todo descolgado", "Tout est décroché"), () =>
            {
                foreach (var path in paths)
                    if (File.Exists(path)) Add(path, Items.Count, true, false, overflow: true);
            });
        }

        /// <summary>Photos whose file was deleted or moved away fall off by themselves, and are forgotten.</summary>
        public void Prune()
        {
            foreach (var item in Items.Where(i => !i.Falling).ToList())
                if (!File.Exists(item.Path)) Drop(item.Id, quietly: true);
            int before = archive.Count + ahead.Count;
            archive.RemoveAll(p => !File.Exists(p));
            ahead.RemoveAll(p => !File.Exists(p));
            if (archive.Count + ahead.Count != before)
            {
                Save();
                ItemsChanged?.Invoke();
            }
        }

        /// <summary>After editing, the photo on the line shows the new version.</summary>
        public void ReloadChanged()
        {
            foreach (var item in Items.Where(i => !i.Falling).ToList())
            {
                var written = LastWriteOf(item.Path);
                if (written == item.LastWrite || written == DateTime.MinValue) continue;
                if (!Imaging.IsReady(item.Path)) continue; // still being saved
                var thumb = Imaging.LoadThumbnail(item.Path, 480, out _, out _);
                if (thumb == null) continue;
                item.LastWrite = written;
                item.Thumb = thumb;
                ItemUpdated?.Invoke(item);
            }
        }

        // MARK: Looking back

        /// <summary>
        /// Brings the most recent capture that fell off back in at the far end.
        /// When the line is full, the newest one steps out at the near end until
        /// you scroll back.
        /// </summary>
        public bool ScrollOlder()
        {
            archive.RemoveAll(p => !File.Exists(p));
            if (archive.Count == 0) return false;
            var path = archive[0];
            archive.RemoveAt(0);
            var live = Live;
            if (live.Count >= MaxItems && live.Count > 0)
            {
                var newest = live[live.Count - 1];
                Forget(newest.Id);
                Items.Remove(newest);
                ahead.Insert(0, newest.Path);
            }
            int first = Items.FindIndex(i => !i.Falling);
            if (Add(path, first < 0 ? Items.Count : first, true, false, overflow: false) == null)
            {
                Save();
                ItemsChanged?.Invoke();
            }
            return true;
        }

        public bool ScrollNewer()
        {
            ahead.RemoveAll(p => !File.Exists(p));
            if (ahead.Count == 0) return false;
            var path = ahead[0];
            ahead.RemoveAt(0);
            var live = Live;
            if (live.Count >= MaxItems && live.Count > 0)
            {
                var oldest = live[0];
                Forget(oldest.Id);
                Items.Remove(oldest);
                Remember(oldest.Path);
            }
            if (Add(path, Items.Count, true, false, overflow: false) == null)
            {
                Save();
                ItemsChanged?.Invoke();
            }
            return true;
        }

        public void BackToNewest()
        {
            while (ahead.Count > 0) ScrollNewer();
        }

        // MARK: Actions on one photo

        /// <summary>Puts the image on the clipboard as a picture and as a file, so it pastes into an app or a folder alike.</summary>
        public void Copy(Guid id)
        {
            var item = Find(id);
            if (item == null || !FileActions.Copy(item.Path)) return;
            CopiedId = id;
            Delay.Run(1.2, () => { if (CopiedId == id) CopiedId = null; });
        }

        public void Open(Guid id)
        {
            var item = Find(id);
            if (item != null) Shell.Open(item.Path);
        }

        /// <summary>Press and hold: mark it up in the built-in editor.</summary>
        public void Edit(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            if (OpenEditor != null) OpenEditor(item.Path);
            else Shell.Edit(item.Path);
        }

        /// <summary>The image editor Windows uses for this kind of file, Paint unless chosen otherwise.</summary>
        public void EditElsewhere(Guid id)
        {
            var item = Find(id);
            if (item != null) Shell.Edit(item.Path);
        }

        public void Reveal(Guid id)
        {
            var item = Find(id);
            if (item != null) Shell.Reveal(item.Path);
        }

        /// <summary>
        /// Takes the photo off the line and sends the file to the Recycle Bin,
        /// once the Undo on offer has run out.
        /// </summary>
        public void Trash(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            string path = item.Path;
            int at = Items.IndexOf(item);
            if (SoundOn) Sounds.Recycle();
            Drop(id, quietly: true);
            Undo.Offer(Loc.L("Moved to the Recycle Bin", "Movida a la Papelera de reciclaje", "Placée dans la Corbeille"),
                       () => Add(path, at, true, false, overflow: true),
                       () => FileActions.Recycle(path));
        }

        /// <summary>
        /// Whether the file lives in Pegline's inbox. Those are discarded to the
        /// Recycle Bin, or the folder would fill up with forgotten screenshots.
        /// Files anywhere else stay where they are.
        /// </summary>
        public bool IsInInbox(Guid id)
        {
            var item = Find(id);
            return item != null && Inbox.Contains(item.Path);
        }

        /// <summary>The corner cross and "Take down" both end up here.</summary>
        public void Discard(Guid id)
        {
            if (IsInInbox(id))
            {
                Trash(id);
                return;
            }
            var item = Find(id);
            if (item == null) return;
            string path = item.Path;
            int at = Items.IndexOf(item);
            Drop(id);
            Undo.Offer(Loc.L("Taken down", "Descolgada", "Décrochée"), () => Add(path, at, true, false, overflow: true));
        }

        /// <summary>Inbox mode: keep a screenshot by moving it to the Screenshots folder.</summary>
        public void Save(Guid id)
        {
            var item = Find(id);
            if (item == null || Keep == null) return;
            if (Keep(item.Path) != null) Drop(id, quietly: true);
            else SystemSounds.Beep.Play();
        }

        // MARK: Breeze

        /// <summary>
        /// Every so often a little wind moves the line. It is the detail that
        /// makes it feel like an object and not a widget.
        /// </summary>
        void ScheduleGust()
        {
            Delay.Run(7 + random.NextDouble() * 9, () =>
            {
                // Only while the line is down: nobody sees a breeze above the screen.
                if (Items.Count > 0 && DraggingId == null && Revealed) Gust?.Invoke();
                ScheduleGust();
            });
        }

        // MARK: Persistence

        void Save()
        {
            // Newer captures out of view while looking back still belong on the line.
            var paths = Items.Where(i => !i.Falling).Select(i => i.Path).Concat(ahead).ToArray();
            Settings.SetStrings(StoreKey, paths);
            Settings.SetStrings(ArchiveKey, archive.ToArray());
        }

        void Restore()
        {
            foreach (var path in Settings.GetStrings(ArchiveKey))
                if (File.Exists(path) && archive.Count < ArchiveLimit) archive.Add(path);
            foreach (var path in Settings.GetStrings(StoreKey))
                if (File.Exists(path)) Hang(path, quietly: true);
        }

        // MARK: Helpers

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        static DateTime LastWriteOf(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        static DateTime CreatedOf(string path)
        {
            try { return File.Exists(path) ? File.GetCreationTimeUtc(path) : DateTime.UtcNow; }
            catch { return DateTime.UtcNow; }
        }
    }

    /// <summary>What can be done to a screenshot file, wherever it is shown: on the line or pinned.</summary>
    static class FileActions
    {
        /// <summary>Puts the image on the clipboard as a picture and as a file, so it pastes into an app or a folder alike.</summary>
        public static bool Copy(string path)
        {
            try
            {
                var data = new DataObject();
                var png = Imaging.PngBytes(path);
                if (png != null) data.SetData("PNG", new MemoryStream(png), false);
                var full = Imaging.LoadFull(path);
                if (full != null) data.SetImage(full);
                data.SetFileDropList(new StringCollection { path });
                Retry(() => Clipboard.SetDataObject(data, true));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not copy", e);
                SystemSounds.Beep.Play();
                return false;
            }
        }

        /// <summary>
        /// Sends the file to the Recycle Bin with the standard dialogs: Windows
        /// asks only if the user wants it to, and warns before deleting anything
        /// it cannot recycle.
        /// </summary>
        public static bool Recycle(string path)
        {
            try
            {
                FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                Log.Info("Recycled " + Path.GetFileName(path));
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                Log.Error("Could not recycle " + path, e);
                SystemSounds.Beep.Play();
                return false;
            }
        }

        /// <summary>Another app can hold the clipboard for a moment; try again briefly.</summary>
        static void Retry(Action action)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (COMException) when (attempt < 6)
                {
                    Thread.Sleep(40);
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Pegline
{
    /// <summary>
    /// Inbox mode: Pegline takes care of your screenshots.
    ///
    /// New captures are moved out of the Screenshots folder into Pegline's own
    /// folder the moment they are saved, and snips that were only copied to
    /// the clipboard are caught too. Only what you keep, by dragging it out or
    /// saving it, ends up back with your pictures. Discarding sends a capture
    /// to the Recycle Bin, so nothing is ever lost for good.
    ///
    /// Unlike the macOS original, no system setting is changed: there is
    /// nothing to put back when the mode is turned off or Pegline quits.
    /// </summary>
    static class Inbox
    {
        public static string Folder => Path.Combine(Settings.DataFolder, "Screenshots");

        public static bool IsEnabled
        {
            get => Settings.GetBool("InboxEnabled");
            set => Settings.SetBool("InboxEnabled", value);
        }

        /// <summary>Whether we already asked, so the offer appears only once.</summary>
        public static bool WasOffered
        {
            get => Settings.GetBool("InboxOffered");
            set => Settings.SetBool("InboxOffered", value);
        }

        public static bool Contains(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                return full.StartsWith(Path.GetFullPath(Folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Moves a fresh capture into the inbox. Null when the file is still busy.</summary>
        public static string MoveIn(string path)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var target = UniquePath(Folder, Path.GetFileName(path));
                File.Move(path, target);
                Log.Info($"Moved {Path.GetFileName(path)} into the inbox");
                return target;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Saves the image on the clipboard as a PNG in the inbox, named the way Windows names screenshots.</summary>
        public static string SaveClipboardImage()
        {
            byte[] png = null;
            for (int attempt = 0; attempt < 5 && png == null; attempt++)
            {
                try
                {
                    png = ReadClipboardPng();
                }
                catch (Exception e)
                {
                    if (attempt == 4) Log.Error("Could not read the clipboard", e);
                    Thread.Sleep(60);
                }
            }
            if (png == null) return null;

            try
            {
                Directory.CreateDirectory(Folder);
                var name = $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png";
                var target = UniquePath(Folder, name);
                File.WriteAllBytes(target, png);
                Log.Info("Saved a copied snip as " + Path.GetFileName(target));
                return target;
            }
            catch (Exception e)
            {
                Log.Error("Could not save a copied snip", e);
                return null;
            }
        }

        static byte[] ReadClipboardPng()
        {
            if (Clipboard.GetData("PNG") is MemoryStream stream && stream.Length > 0)
                return stream.ToArray();

            // CF_DIB through Windows Forms is the most forgiving decoder.
            using (var image = System.Windows.Forms.Clipboard.GetImage())
            {
                if (image == null) return null;
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        public static string UniquePath(string folder, string name)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            var candidate = Path.Combine(folder, name);
            for (int n = 2; File.Exists(candidate); n++)
                candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            return candidate;
        }
    }
}

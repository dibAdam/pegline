using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace Pegline
{
    static class Imaging
    {
        public static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".webp", ".jxr"
        };

        public static bool IsImage(string path)
        {
            var name = Path.GetFileName(path);
            return !string.IsNullOrEmpty(name) && !name.StartsWith(".") && !name.StartsWith("~")
                && Extensions.Contains(Path.GetExtension(path));
        }

        static FileStream OpenShared(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        /// <summary>
        /// True once nobody is writing the file any more: it opens without
        /// sharing write access, has content and its header decodes.
        /// </summary>
        public static bool IsReady(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (fs.Length == 0) return false;
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    return decoder.Frames.Count > 0 && decoder.Frames[0].PixelWidth > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (var fs = OpenShared(path))
                {
                    var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                    return width > 0 && height > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A downscaled copy whose longer side is at most maxPixels. The file is not kept open.</summary>
        public static BitmapSource LoadThumbnail(string path, int maxPixels, out int width, out int height)
        {
            width = height = 0;
            try
            {
                if (!TryReadSize(path, out width, out height)) return null;
                using (var fs = OpenShared(path))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.StreamSource = fs;
                    if (width >= height) image.DecodePixelWidth = Math.Min(width, maxPixels);
                    else image.DecodePixelHeight = Math.Min(height, maxPixels);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not read " + path, e);
                return null;
            }
        }

        public static BitmapSource LoadFull(string path)
        {
            try
            {
                using (var fs = OpenShared(path))
                {
                    var frame = BitmapFrame.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not read " + path, e);
                return null;
            }
        }

        public static byte[] PngBytes(string path)
        {
            try
            {
                if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    using (var fs = OpenShared(path))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
                var full = LoadFull(path);
                return full == null ? null : Encode(full);
            }
            catch (Exception e)
            {
                Log.Error("Could not read " + path, e);
                return null;
            }
        }

        public static byte[] Encode(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }
    }
}

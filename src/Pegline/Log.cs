using System;
using System.Diagnostics;
using System.IO;

namespace Pegline
{
    /// <summary>A plain text log in %LOCALAPPDATA%\Pegline, kept under half a megabyte.</summary>
    static class Log
    {
        static readonly object gate = new object();

        static string LogPath => Path.Combine(Settings.DataFolder, "pegline.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message, Exception e = null) =>
            Write("ERROR", e == null ? message : $"{message}: {e.GetType().Name}: {e.Message}\r\n{e.StackTrace}");

        static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
            Debug.WriteLine(line);
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.DataFolder);
                    var path = LogPath;
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        var old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}

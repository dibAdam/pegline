using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Interop;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>An invisible, message-only window for the hotkey and the clipboard.</summary>
    sealed class MessageWindow : IDisposable
    {
        readonly HwndSource source;

        public IntPtr Handle => source.Handle;

        public MessageWindow()
        {
            source = new HwndSource(new HwndSourceParameters("PeglineMessages")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            });
        }

        public void AddHook(HwndSourceHook hook) => source.AddHook(hook);

        public void Dispose() => source.Dispose();
    }

    /// <summary>A keyboard shortcut: modifier flags as RegisterHotKey wants them, and a virtual key.</summary>
    readonly struct Shortcut
    {
        public readonly uint Modifiers, Key;

        public Shortcut(uint modifiers, uint key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public static readonly Shortcut Default = new Shortcut(MOD_CONTROL | MOD_ALT, 0x54); // Ctrl+Alt+T
        public bool IsEmpty => Key == 0;

        /// <summary>The shortcut chosen in Settings, or Ctrl+Alt+T. Stored as modifiers in the high word, key in the low word; -1 means none.</summary>
        public static Shortcut Load()
        {
            int stored = Settings.GetInt("HotKey", int.MinValue);
            if (stored == int.MinValue) return Default;
            if (stored == -1) return new Shortcut(0, 0);
            return new Shortcut((uint)(stored >> 16) & 0xFFFF, (uint)stored & 0xFFFF);
        }

        public void Save() => Settings.SetInt("HotKey", IsEmpty ? -1 : (int)((Modifiers << 16) | Key));

        public override string ToString()
        {
            if (IsEmpty) return Loc.L("None", "Ninguno", "Aucun");
            var parts = new List<string>();
            if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & 4) != 0) parts.Add(Loc.L("Shift", "Mayús", "Maj"));
            if ((Modifiers & 8) != 0) parts.Add("Win");
            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)Key);
            string name = key.ToString();
            if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9) name = name.Substring(1);
            parts.Add(name);
            return string.Join("+", parts);
        }
    }

    /// <summary>
    /// A single global shortcut through RegisterHotKey. Unlike a keyboard hook,
    /// it needs no special permission and costs nothing while idle.
    /// </summary>
    sealed class HotKey : IDisposable
    {
        readonly MessageWindow window;
        readonly int id;
        readonly Action action;

        public bool IsRegistered { get; }

        bool disposed;

        public Shortcut Shortcut { get; }

        public HotKey(MessageWindow window, int id, Shortcut shortcut, Action action)
        {
            this.window = window;
            this.id = id;
            this.action = action;
            Shortcut = shortcut;
            window.AddHook(Hook);
            if (shortcut.IsEmpty) return;
            IsRegistered = RegisterHotKey(window.Handle, id, shortcut.Modifiers | MOD_NOREPEAT, shortcut.Key);
            if (!IsRegistered) Log.Info($"The shortcut {shortcut} is taken by another app");
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!disposed && msg == WM_HOTKEY && wParam.ToInt32() == id)
            {
                handled = true;
                action();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            disposed = true;
            if (IsRegistered) UnregisterHotKey(window.Handle, id);
        }
    }

    sealed class ClipboardCapture
    {
        public DateTime Time;
        public POINT Cursor;
        public uint Sequence;
        public string Tool;
    }

    /// <summary>
    /// Notices when a screen capture tool puts an image on the clipboard. The
    /// moment of the snip and where the pointer was tell us where the capture
    /// was taken, and in inbox mode a snip that was only copied still hangs.
    /// </summary>
    sealed class ClipboardWatcher : IDisposable
    {
        readonly MessageWindow window;
        readonly uint ownProcess = (uint)Process.GetCurrentProcess().Id;
        readonly uint pngFormat = RegisterClipboardFormat("PNG");
        readonly uint htmlFormat = RegisterClipboardFormat("HTML Format");
        readonly bool listening;

        /// <summary>Process names of tools that put screenshots on the clipboard.</summary>
        static readonly HashSet<string> CaptureTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SnippingTool", "ScreenClippingHost", "ScreenSketch", "ShareX", "Greenshot", "Lightshot",
            "flameshot", "PicPick", "Snagit32", "SnagitEditor", "Screenpresso", "FastStone Capture", "FSCapture"
        };

        public event Action<ClipboardCapture> Captured;

        public ClipboardWatcher(MessageWindow window)
        {
            this.window = window;
            window.AddHook(Hook);
            listening = AddClipboardFormatListener(window.Handle);
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE) Check();
            return IntPtr.Zero;
        }

        void Check()
        {
            try
            {
                bool image = IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_DIBV5)
                          || IsClipboardFormatAvailable(CF_BITMAP) || IsClipboardFormatAvailable(pngFormat);
                if (!image) return;

                var owner = GetClipboardOwner();
                uint pid = owner == IntPtr.Zero ? 0 : ProcessOf(owner);
                if (pid == ownProcess) return;

                string tool = null;
                if (pid != 0)
                {
                    try { tool = Process.GetProcessById((int)pid).ProcessName; }
                    catch { }
                }

                // Print Screen on its own leaves a bare bitmap with no owner. A
                // picture copied from a browser or a document brings HTML or a
                // file along with it, so those are not screenshots.
                bool rich = IsClipboardFormatAvailable(htmlFormat) || IsClipboardFormatAvailable(CF_HDROP);
                bool isCapture = tool != null ? CaptureTools.Contains(tool) : !rich;
                if (!isCapture) return;

                Captured?.Invoke(new ClipboardCapture
                {
                    Time = DateTime.UtcNow,
                    Cursor = Cursor(),
                    Sequence = GetClipboardSequenceNumber(),
                    Tool = tool ?? "Print Screen"
                });
            }
            catch (Exception e)
            {
                Log.Error("Clipboard check failed", e);
            }
        }

        public void Dispose()
        {
            if (listening) RemoveClipboardFormatListener(window.Handle);
        }
    }
}

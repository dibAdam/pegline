using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using static Pegline.Loc;
using static Pegline.Native;

namespace Pegline
{
    /// <summary>
    /// Runs the app: the tray icon, the shortcut, the watchers, and when the
    /// line comes down and when it tucks itself away.
    /// </summary>
    sealed class Controller
    {
        readonly Options options;
        Line line;
        LineWindow panel;
        PinBoard pins;
        UndoToast toast;
        TabWindow tab;
        Tray tray;
        MessageWindow messages;
        HotKey hotKey;
        ClipboardWatcher clipboard;
        ScreenshotWatcher watcher;
        DispatcherTimer mouseTimer, housekeeping;
        WinEventProc foregroundProc;
        IntPtr foregroundHook;

        /// <summary>Whether the window is shown. It can be shown and still tucked away above the top edge.</summary>
        bool isPresent;
        /// <summary>Whether the line has slid down into view.</summary>
        bool isRevealed;
        /// <summary>Opened on purpose with the shortcut or the tray: it stays down until the pointer has visited it and left, or the shortcut is pressed again.</summary>
        bool pinned;
        /// <summary>A new screenshot shows itself for a moment, then tucks away.</summary>
        DateTime peekUntil = DateTime.MinValue;
        DateTime? hotZoneSince, awaySince;
        /// <summary>Where the pointer started resting against the top edge.</summary>
        POINT? hotZoneAnchor;
        /// <summary>After a click along the top of the screen the line stays up there hidden until the pointer leaves that band, so it does not come down over tabs or a title bar you are using.</summary>
        bool topBandSuppressed;
        /// <summary>Whether the line should be shown, if nothing prevents it. A full screen app on that display does: the line waits until it leaves full screen.</summary>
        bool wanted;
        /// <summary>Set when you open the line on purpose, so it stays up while empty.</summary>
        bool keepOpen;
        int lastLiveCount;
        bool wasDown;
        int releasedTicks;
        DateTime lastFullScreenCheck;
        /// <summary>The display a new capture was taken on: the line goes there.</summary>
        MonitorInfo pendingMonitor;
        ClipboardCapture lastCapture;
        DateTime lastFileArrival = DateTime.MinValue;

        // Settings read 30 times a second are kept here, and refreshed from ApplySettings.
        bool edgeReveal, pullTab;

        /// <summary>How long the pointer rests against the top edge before the line comes down. It has to be still, so this can be short.</summary>
        static readonly TimeSpan RevealDelay = TimeSpan.FromSeconds(0.15);
        const double FlightHeadStart = 0.15;
        /// <summary>How long the pointer is away before the line tucks back up.</summary>
        static readonly TimeSpan RetractDelay = TimeSpan.FromSeconds(0.4);

        public Controller(Options options)
        {
            this.options = options;
        }

        public void Start()
        {
            line = new Line { Keep = KeepInScreenshots, OpenEditor = OpenEditor };
            panel = new LineWindow(line);
            panel.PlaceOnScreen();
            pins = new PinBoard(line, HangFrom);
            toast = new UndoToast();
            panel.PinRequested += (item, from, follow) =>
            {
                line.Detach(item.Id);
                pins.Pin(item.Path, from, follow);
            };
            tab = new TabWindow();
            tab.Opened += () => { if (!isRevealed) Open(keyboard: false); };
            panel.CloseRequested += () => { if (isRevealed) Toggle(); };
            // The windows for flights and drags are made now, while nothing moves,
            // so the first capture or drag does not wait for them.
            Delay.Run(1.5, () =>
            {
                Flight.Prewarm();
                DragGhost.Prewarm();
            });

            messages = new MessageWindow();
            hotKey = new HotKey(messages, 1, Shortcut.Load(), ShortcutPressed);
            clipboard = new ClipboardWatcher(messages);
            clipboard.Captured += OnClipboardCapture;

            StartWatcher();
            tray = new Tray(Toggle, BuildMenu);
            ApplySettings();

            line.OnFall = Fall;
            line.ItemsChanged += ItemsChanged;
            // Photos restored from last time peek in, like a fresh one would.
            ItemsChanged();

            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatch(ScreensChanged);
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Desktop) Dispatch(ScreensChanged);
            };

            // Switching apps can bring a full screen app forward. Check again once
            // the switch has settled.
            foregroundProc = (hook, ev, hwnd, idObject, idChild, thread, time) =>
            {
                Refresh();
                Delay.Run(0.8, Refresh);
            };
            foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                             foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Edited files get a fresh thumbnail; deleted ones fall off.
            housekeeping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            housekeeping.Tick += (s, e) =>
            {
                line.Prune();
                line.ReloadChanged();
                panel.SetLights(LightsWanted());
            };
            housekeeping.Start();

            LaunchAtLogin.Repair();
            StartPreviews();

            if (!options.Quiet)
            {
                if (!Settings.GetBool("Toured"))
                {
                    // First run: a short tour, then the one question, then a pointer to the tray icon.
                    Delay.Run(1.0, () => TourWindow.ShowTour(() =>
                    {
                        Settings.SetBool("Toured", true);
                        if (!Inbox.WasOffered) OfferInbox();
                        ShowTrayTip();
                    }));
                }
                else if (!Inbox.WasOffered)
                {
                    Delay.Run(1.2, OfferInbox);
                }
            }

            if (!Settings.GetBool("Welcomed") && !options.Quiet)
            {
                Settings.SetBool("Welcomed", true);
                keepOpen = true;
                wanted = true;
                Refresh();
                Reveal(pinned: true);
                Delay.Run(5, () =>
                {
                    if (line.LiveCount != 0) return;
                    keepOpen = false;
                    wanted = false;
                    Refresh();
                });
            }
            Log.Info("Started");
        }

        public void Stop()
        {
            try
            {
                // Anything still waiting on an Undo becomes final now.
                Undo.Commit();
                tray?.Dispose();
                hotKey?.Dispose();
                clipboard?.Dispose();
                watcher?.Dispose();
                if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
                messages?.Dispose();
            }
            catch (Exception e)
            {
                Log.Error("Error while quitting", e);
            }
            Log.Info("Stopped");
        }

        static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

        /// <summary>Development switches that show one part of the app, for screenshots and testing.</summary>
        void StartPreviews()
        {
            Action openLine = () =>
            {
                keepOpen = true;
                wanted = true;
                panel.PlaceOnScreen(Monitors.Primary);
                Refresh();
                Reveal(pinned: true);
            };
            if (options.Demo)
                Delay.Run(1, () =>
                {
                    openLine();
                    DemoStep(0);
                });
            if (options.PreviewPin)
                Delay.Run(2.5, () =>
                {
                    openLine();
                    Delay.Run(0.8, panel.PreviewPin);
                });
            if (options.PreviewMenu)
                Delay.Run(0.8, () =>
                {
                    var primary = Monitors.Primary;
                    var dpi = primary?.Scale ?? 1;
                    var center = primary?.Bounds.Center ?? new POINT(600, 400);
                    Menus.Show(BuildMenu(), new Point(center.X / dpi, center.Y / dpi));
                });
            switch (options.Preview)
            {
                case "settings": Delay.Run(0.8, () => SettingsWindow.ShowFor(this)); break;
                case "tour": Delay.Run(0.8, () => TourWindow.ShowTour()); break;
                case "editor": Delay.Run(2.5, () => { var first = line.Live.FirstOrDefault(); if (first != null) OpenEditor(first.Path); }); break;
                case "editortest": Delay.Run(2.5, () => { var first = line.Live.FirstOrDefault(); if (first != null) MarkupWindow.Exercise(first.Path); }); break;
                case "preview": Delay.Run(2.5, () => { openLine(); Delay.Run(0.8, panel.PreviewFirst); }); break;
                case "keyboard": Delay.Run(2.5, () => Open(keyboard: true)); break;
                case "undo": Delay.Run(2.5, () => { var first = line.Live.FirstOrDefault(); if (first != null) line.Discard(first.Id); }); break;
                case "tab": Delay.Run(1.5, () => { peekUntil = DateTime.MinValue; SetRevealed(false); }); break;
                case "history":
                    Delay.Run(2.5, () =>
                    {
                        openLine();
                        Action report = () => Log.Info($"History: on line [{string.Join(", ", line.Live.Select(i => Path.GetFileNameWithoutExtension(i.Path)))}], {line.OlderCount} older, {line.NewerCount} newer");
                        report();
                        line.ScrollOlder(); report();
                        line.ScrollOlder(); report();
                        line.ScrollOlder(); report();
                        line.ScrollNewer(); report();
                        line.BackToNewest(); report();
                    });
                    break;
            }
        }

        void DemoStep(int step)
        {
            panel.Demo(step);
            Delay.Run(2.2, () => DemoStep(step + 1));
        }

        /// <summary>Fairy lights after sunset, unless switched off.</summary>
        bool LightsWanted() => options.Night || (!Settings.GetBool("NightLightsOff") && Night.IsDark(DateTime.Now));

        /// <summary>A second launch, or the Start menu entry while running: bring the line down.</summary>
        public void ShowFromOutside()
        {
            if (!isRevealed) Open(keyboard: false);
        }

        // MARK: Settings

        public string WatchFolder => watcher?.Folder ?? "";
        public Shortcut Shortcut => hotKey?.Shortcut ?? Shortcut.Load();
        public bool ShortcutWorks => hotKey != null && hotKey.IsRegistered;

        /// <summary>Re-reads the settings that change behaviour while running.</summary>
        public void ApplySettings()
        {
            edgeReveal = !Settings.GetBool("EdgeRevealOff");
            pullTab = !Settings.GetBool("PullTabOff");
            UpdateCapacity();
            UpdateTab();
            panel.SetLights(LightsWanted());
        }

        /// <summary>Switches to a new shortcut, or keeps the old one if Windows refuses it.</summary>
        public bool TrySetShortcut(Shortcut shortcut)
        {
            var old = hotKey.Shortcut;
            hotKey.Dispose();
            hotKey = new HotKey(messages, 1, shortcut, ShortcutPressed);
            if (shortcut.IsEmpty || hotKey.IsRegistered)
            {
                shortcut.Save();
                return true;
            }
            hotKey.Dispose();
            hotKey = new HotKey(messages, 1, old, ShortcutPressed);
            return false;
        }

        public void OpenScreenshotsFolder() => Shell.OpenFolder(Inbox.IsEnabled ? Inbox.Folder : watcher.Folder);

        public void ChooseFolder()
        {
            var picked = FolderPicker.Pick(L("Choose the folder your screenshots are saved to",
                                             "Elige la carpeta donde se guardan tus capturas",
                                             "Choisissez le dossier où sont enregistrées vos captures"), watcher.Folder);
            if (picked == null) return;
            Settings.SetString("WatchFolder", picked);
            StartWatcher();
        }

        public void ResetFolder()
        {
            Settings.SetString("WatchFolder", null);
            StartWatcher();
        }

        void ShowTrayTip()
        {
            if (Settings.GetBool("TrayTipShown")) return;
            Settings.SetBool("TrayTipShown", true);
            tray.ShowTip(L("Pegline is running", "Pegline está en marcha", "Pegline est lancé"),
                         L("Right-click its icon for settings. Windows may hide it under ^ in the taskbar: drag it out to keep it in view.",
                           "Haz clic derecho en su icono para la configuración. Windows puede ocultarlo bajo ^ en la barra de tareas: arrástralo fuera para tenerlo a la vista.",
                           "Clic droit sur son icône pour les paramètres. Windows peut la cacher sous ^ dans la barre des tâches : faites-la glisser pour la garder visible."));
        }

        // MARK: Screenshots arriving

        void StartWatcher()
        {
            watcher?.Dispose();
            var folder = options.Folder ?? ScreenshotWatcher.ScreenshotsFolder();
            watcher = new ScreenshotWatcher(folder, OnNewScreenshot, () => line.Prune());
            watcher.Start();
        }

        void OnNewScreenshot(string path)
        {
            lastFileArrival = DateTime.UtcNow;
            var capture = RecentCapture();
            WhenReady(path, 0, () =>
            {
                if (Inbox.IsEnabled) IntoInbox(path, 0, moved => HangCapture(moved, capture));
                else HangCapture(path, capture);
            });
        }

        /// <summary>Waits until whoever is writing the file has finished.</summary>
        static void WhenReady(string path, int attempt, Action then)
        {
            if (!File.Exists(path)) return;
            if (Imaging.IsReady(path))
            {
                then();
                return;
            }
            if (attempt >= 40)
            {
                Log.Info("Gave up waiting for " + Path.GetFileName(path));
                return;
            }
            Delay.Run(0.25, () => WhenReady(path, attempt + 1, then));
        }

        /// <summary>Moves a capture into the inbox, retrying while a sync client still has it open.</summary>
        static void IntoInbox(string path, int attempt, Action<string> then)
        {
            var moved = Inbox.MoveIn(path);
            if (moved != null || !File.Exists(path))
            {
                then(moved ?? path);
                return;
            }
            if (attempt >= 12)
            {
                Log.Info("Could not move " + Path.GetFileName(path) + " into the inbox; it hangs where it is");
                then(path);
                return;
            }
            Delay.Run(0.25, () => IntoInbox(path, attempt + 1, then));
        }

        ClipboardCapture RecentCapture() =>
            lastCapture != null && (DateTime.UtcNow - lastCapture.Time).TotalSeconds < 4 ? lastCapture : null;

        void OnClipboardCapture(ClipboardCapture capture)
        {
            // Capture tools often write the clipboard more than once per snip.
            if (lastCapture != null && (capture.Time - lastCapture.Time).TotalSeconds < 1)
            {
                lastCapture.Sequence = capture.Sequence;
                return;
            }
            lastCapture = capture;
            Log.Info("Snip from " + capture.Tool);
            if (!Inbox.IsEnabled) return;

            // In inbox mode a snip that was only copied still hangs, unless the
            // tool saved a file as well, which the folder watcher already has.
            Delay.Run(0.9, () =>
            {
                if (lastFileArrival >= capture.Time.AddSeconds(-1)) return;
                if (GetClipboardSequenceNumber() != capture.Sequence) return;
                var path = Inbox.SaveClipboardImage();
                if (path != null) HangCapture(path, capture);
            });
        }

        /// <summary>
        /// A new screenshot lifts off from where it was taken and flies to its
        /// place on the line. Without a known capture area it simply drops in.
        /// </summary>
        void HangCapture(string path, ClipboardCapture capture)
        {
            PxRect? from = null;
            if (Imaging.TryReadSize(path, out int w, out int h))
                from = CaptureGuess.Guess(w, h, capture?.Cursor ?? Cursor(), nearCapture: capture != null);
            var monitor = from.HasValue ? Monitors.At(from.Value.Center) : capture != null ? Monitors.At(capture.Cursor) : null;
            HangPrepared(path, from, monitor);
        }

        /// <summary>A pin goes back on the line: it flies up from where it was stuck.</summary>
        void HangFrom(string path, PxRect from) => HangPrepared(path, from, Monitors.At(from.Center));

        /// <summary>
        /// Decodes the card and the flying image on a worker thread first, so
        /// the line and the flight start without a stall, then hangs it.
        /// </summary>
        void HangPrepared(string path, PxRect? from, MonitorInfo monitor)
        {
            int pixels = from.HasValue ? Math.Min(2000, Math.Max(400, Math.Max(from.Value.Width, from.Value.Height))) : 0;
            Background.Run(() =>
            {
                var thumb = Imaging.LoadThumbnail(path, 480, out _, out _);
                var flying = thumb != null && pixels > 0 ? Imaging.LoadThumbnail(path, pixels, out _, out _) : null;
                return (thumb, flying);
            }, prepared =>
            {
                if (prepared.thumb == null) return;
                pendingMonitor = monitor;
                bool flies = from.HasValue && prepared.flying != null;
                bool wasDown = isRevealed;
                var id = Animator.Timed("hang", () => line.Hang(path, flying: flies, thumb: prepared.thumb));
                if (id == null || !flies) return;
                // Let the line lay out before measuring the landing spot. A line
                // that was tucked away gets a head start: moving it while the
                // photo flies slowed both.
                var rect = from.Value;
                Delay.Run(wasDown ? 0.02 : FlightHeadStart, () => Fly(id.Value, rect, prepared.flying));
            });
        }

        void Fly(Guid id, PxRect from, BitmapSource image)
        {
            var item = line.Find(id);
            var to = panel.CardFrame(id);
            var monitor = panel.Monitor;
            if (!isPresent || !isRevealed || item == null || to == null || monitor == null)
            {
                line.Land(id);
                return;
            }
            Animator.Timed("flight start", () => Flight.Fly(image ?? item.Thumb, from, to.Value, item.Tilt, monitor, () => line.Land(id)));
        }

        /// <summary>A discarded card falls over the whole screen, from where it hangs.</summary>
        void Fall(Pegged item)
        {
            if (!isPresent || !isRevealed || item.Flying || panel.Monitor == null) return;
            var card = panel.CardFrame(item.Id);
            if (card != null) Flight.Fall(item.Thumb, card.Value, item.Tilt, panel.Monitor);
        }

        /// <summary>The built-in markup editor. Saving refreshes the photo on the line right away.</summary>
        void OpenEditor(string path)
        {
            // The line steps aside first, so the editor opens in front with the keyboard.
            if (isRevealed) SetRevealed(false);
            MarkupWindow.Open(path, saved => line.ReloadChanged());
        }

        /// <summary>Inbox mode: a kept capture goes to the Screenshots folder, where Windows keeps them.</summary>
        string KeepInScreenshots(string path)
        {
            try
            {
                var folder = watcher.Folder;
                Directory.CreateDirectory(folder);
                var target = Inbox.UniquePath(folder, Path.GetFileName(path));
                watcher.Ignore(target);
                File.Move(path, target);
                return target;
            }
            catch (Exception e)
            {
                Log.Error("Could not save to the Screenshots folder", e);
                return null;
            }
        }

        // MARK: Inbox mode

        public void SetInbox(bool on)
        {
            Inbox.IsEnabled = on;
            Log.Info("Inbox mode " + (on ? "on" : "off"));
        }

        /// <summary>Asked once. How screenshots are handled is the user's call, never ours.</summary>
        void OfferInbox()
        {
            Inbox.WasOffered = true;
            bool yes = Prompt.Ask(
                L("Let Pegline handle your screenshots?",
                  "¿Quieres que Pegline se encargue de tus capturas?",
                  "Laisser Pegline s’occuper de vos captures ?"),
                L("Screenshots will hang on the line the moment you take them, and they won't pile up in your Screenshots folder. Snips you only copy with Win + Shift + S are caught too. Drag one to a folder to keep it, or discard it with the cross. You can change this in Settings.",
                  "Las capturas se colgarán en cuanto las hagas y no se acumularán en tu carpeta de Capturas. También se recogen los recortes que solo copias con Win + Mayús + S. Arrastra una a una carpeta para guardarla, o descártala con la cruz. Puedes cambiarlo en la configuración.",
                  "Vos captures s’accrocheront au fil dès que vous les prenez, sans s’entasser dans votre dossier Captures d’écran. Les captures simplement copiées avec Win + Maj + S sont récupérées aussi. Glissez-en une dans un dossier pour la garder, ou jetez-la avec la croix. Vous pouvez changer cela dans les paramètres."),
                L("Turn on", "Activar", "Activer"),
                L("Not now", "Ahora no", "Pas maintenant"));
            if (yes) SetInbox(true);
        }

        // MARK: Showing and hiding

        void ItemsChanged()
        {
            int live = line.LiveCount;
            if (live > lastLiveCount)
            {
                panel.PlaceOnScreen(pendingMonitor);
                pendingMonitor = null;
                UpdateCapacity();
                wanted = true;
                Refresh();
                Reveal(peekFor: 2.5);
            }
            else if (live == 0 && !keepOpen)
            {
                Delay.Run(0.7, () =>
                {
                    if (line.LiveCount != 0 || keepOpen) return;
                    wanted = false;
                    Refresh();
                });
            }
            lastLiveCount = live;
            UpdateTab();
        }

        /// <summary>Decides whether the window is shown at all: something to show, and nothing full screen on that display.</summary>
        void Refresh()
        {
            var monitor = Monitors.Find(panel.Monitor?.Device) ?? Monitors.UnderCursor();
            bool blocked = wanted && Animator.Timed("full screen check", () => FullScreen.IsActive(monitor));
            if (wanted && !blocked) Present();
            else Dismiss();
            // The pointer is watched while there is a line, even tucked away,
            // to notice it pushing against the top edge.
            if (wanted) StartMouseTracking();
            else StopMouseTracking();
            UpdateTab();
        }

        void Present()
        {
            if (isPresent) return;
            isPresent = true;
            panel.Show();
            panel.ApplyBounds();
        }

        void Dismiss()
        {
            if (!isPresent) return;
            isPresent = false;
            SetRevealed(false);
            Delay.Run(0.4, () => { if (!isPresent) panel.Hide(); });
        }

        void Reveal(bool pinned = false, double peekFor = 0)
        {
            if (!isPresent) return;
            if (pinned) this.pinned = true;
            if (peekFor > 0) peekUntil = DateTime.UtcNow.AddSeconds(peekFor);
            awaySince = null;
            // Coming down: back on top of other always-on-top windows too.
            if (!isRevealed) panel.ApplyBounds();
            SetRevealed(true);
        }

        void SetRevealed(bool on)
        {
            if (on == isRevealed) return;
            isRevealed = on;
            line.Revealed = on;
            if (!on)
            {
                pinned = false;
                peekUntil = DateTime.MinValue;
                line.HoveredId = null;
                panel.ClickThrough = true;
            }
            UpdateTab();
        }

        /// <summary>The pull tab shows while photos wait on a tucked-away line.</summary>
        void UpdateTab()
        {
            if (tab == null || line == null) return;
            tab.Update(pullTab && isPresent && !isRevealed && line.LiveCount > 0, line.LiveCount,
                       Monitors.Find(panel.Monitor?.Device) ?? panel.Monitor);
            UpdateTickRate();
        }

        /// <summary>
        /// The pointer is watched 60 times a second while the line is down, so a
        /// photo answers the hover at once, and 20 times while it waits above.
        /// </summary>
        void UpdateTickRate()
        {
            if (mouseTimer == null) return;
            var interval = TimeSpan.FromMilliseconds(isRevealed ? 1000.0 / 60 : 1000.0 / 20);
            if (mouseTimer.Interval != interval) mouseTimer.Interval = interval;
        }

        void ShortcutPressed()
        {
            if (isRevealed) Toggle();
            else Open(keyboard: true);
        }

        void Open(bool keyboard)
        {
            keepOpen = true;
            wanted = true;
            panel.PlaceOnScreen();
            UpdateCapacity();
            Refresh();
            Reveal(pinned: true);
            if (keyboard && isRevealed) panel.EnterKeyboard();
        }

        void Toggle()
        {
            if (isRevealed)
            {
                SetRevealed(false);
                if (line.LiveCount == 0)
                {
                    keepOpen = false;
                    wanted = false;
                    Refresh();
                }
            }
            else
            {
                Open(keyboard: false);
            }
        }

        void StartMouseTracking()
        {
            if (mouseTimer != null) return;
            mouseTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(isRevealed ? 1000.0 / 60 : 1000.0 / 20) };
            mouseTimer.Tick += (s, e) =>
            {
                try { Animator.Timed("tick", Tick); }
                catch (Exception ex) { Log.Error("Tick failed", ex); }
            };
            mouseTimer.Start();
        }

        void StopMouseTracking()
        {
            mouseTimer?.Stop();
            mouseTimer = null;
            panel.ClickThrough = true;
        }

        /// <summary>
        /// The strip along the top of a display that counts as "up there": the
        /// height of a title bar, or a taskbar placed at the top. A click in it
        /// puts the line away, like a click in the menu bar on macOS.
        /// </summary>
        static PxRect TopBand(MonitorInfo m)
        {
            int height = Math.Max(m.WorkArea.Y - m.Bounds.Y, (int)Math.Round(40 * m.Scale));
            return new PxRect(m.Bounds.X, m.Bounds.Y, m.Bounds.Width, height);
        }

        /// <summary>
        /// Pushing against the top edge. When another display sits above, the
        /// pointer never stops there, so a slightly deeper strip counts.
        /// </summary>
        static bool AtTopEdge(MonitorInfo m, POINT p)
        {
            bool displayAbove = Monitors.All.Any(o => !o.SameAs(m) && Math.Abs(o.Bounds.Bottom - m.Bounds.Y) <= 1
                                                      && p.X >= o.Bounds.X && p.X < o.Bounds.Right);
            int edge = displayAbove ? (int)Math.Round(10 * m.Scale) : 2;
            return p.Y >= m.Bounds.Y && p.Y < m.Bounds.Y + edge;
        }

        void Tick()
        {
            var mouse = Cursor();
            var now = DateTime.UtcNow;
            var screen = Monitors.At(mouse);
            if (screen == null) return;

            bool down = IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON);
            bool pressedNow = down && !wasDown;
            wasDown = down;

            bool inBand = TopBand(screen).Contains(mouse);
            bool onControl = panel.OverControl(mouse);
            if (pressedNow && inBand && !onControl && !(isRevealed && panel.HitTest(mouse) != null))
            {
                topBandSuppressed = true;
                hotZoneSince = null;
                hotZoneAnchor = null;
                if (isRevealed)
                {
                    pinned = false;
                    SetRevealed(false);
                }
            }
            if (!inBand) topBandSuppressed = false;

            // A press whose release happened away from the photo, where the line
            // never heard about it. Two ticks of grace let a queued release land first.
            if (line.PressedId != null && !down && !CardView.IsDragging)
            {
                if (++releasedTicks > 2) line.PressedId = null;
            }
            else
            {
                releasedTicks = 0;
            }

            if (wanted && (now - lastFullScreenCheck).TotalSeconds >= 1)
            {
                lastFullScreenCheck = now;
                Refresh();
            }

            if (!isRevealed)
            {
                // Resting against the top edge brings the line down on that
                // display. Not while a button is held: that is a window being
                // dragged to the top to snap or maximize. And only at rest:
                // sliding along the edge is someone looking for a browser tab.
                if (edgeReveal && AtTopEdge(screen, mouse) && !down && !topBandSuppressed && !FullScreen.IsActive(screen))
                {
                    int still = (int)Math.Round(12 * screen.Scale);
                    if (hotZoneAnchor == null || Math.Abs(mouse.X - hotZoneAnchor.Value.X) > still)
                    {
                        hotZoneAnchor = mouse;
                        hotZoneSince = now;
                    }
                    if (now - hotZoneSince >= RevealDelay)
                    {
                        hotZoneSince = null;
                        hotZoneAnchor = null;
                        if (!screen.SameAs(panel.Monitor))
                        {
                            panel.PlaceOnScreen(screen);
                            UpdateCapacity();
                        }
                        Refresh();
                        Reveal();
                    }
                }
                else
                {
                    hotZoneSince = null;
                    hotZoneAnchor = null;
                }
                return;
            }

            UpdateMousePassThrough(mouse);
            panel.PointerMoved(mouse);

            // The line's zone runs from its lowest point up to the top of the
            // display, a top taskbar included, so moving up never hides it.
            var frame = panel.PixelBounds;
            int top = (Monitors.Find(panel.Monitor?.Device) ?? screen).Bounds.Y;
            var zone = PxRect.FromLTRB(frame.X, Math.Min(top, frame.Y), frame.Right, frame.Bottom);
            bool inside = zone.Contains(mouse);
            if (inside && pinned) pinned = false;

            bool busy = pinned || options.Demo || panel.IsActive || CardView.IsDragging || line.PressedId != null || now < peekUntil;
            if (inside || busy)
            {
                awaySince = null;
            }
            else
            {
                var since = awaySince ?? now;
                awaySince = since;
                if (now - since >= RetractDelay)
                {
                    awaySince = null;
                    SetRevealed(false);
                }
            }
        }

        /// <summary>
        /// The window spans the whole width of the display, so it only takes
        /// the mouse while the pointer is over a photo or one of its controls.
        /// Everywhere else, clicks go to whatever is underneath.
        /// </summary>
        void UpdateMousePassThrough(POINT mouse)
        {
            if (CardView.IsDragging) return;
            var hit = panel.HitTest(mouse);
            panel.ClickThrough = hit == null && !panel.OverControl(mouse);
            line.HoveredId = hit;
        }

        /// <summary>As many as fit, or the number chosen in Settings if the screen has room for it.</summary>
        void UpdateCapacity()
        {
            var monitor = panel.Monitor;
            double width = monitor != null ? panel.PixelBounds.Width / monitor.Scale : panel.StageWidth;
            int fits = Math.Max(3, Math.Min(12, (int)((width - 40) / Layout.Spacing)));
            int chosen = Settings.GetInt("MaxCards");
            line.MaxItems = chosen > 0 ? Math.Min(chosen, fits) : Math.Max(3, Math.Min(12, (int)((width - 200) / Layout.Spacing)));
        }

        void ScreensChanged()
        {
            Monitors.Refresh();
            panel.PlaceOnScreen(Monitors.Find(panel.Monitor?.Device));
            UpdateCapacity();
            tray?.UpdateImage();
        }

        // MARK: Tray menu

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(Menus.Item(isRevealed ? L("Hide line", "Ocultar tendedero", "Masquer le fil")
                                                 : L("Show line", "Mostrar tendedero", "Afficher le fil"),
                                      Toggle, gesture: ShortcutWorks ? Shortcut.ToString() : null));
            menu.Items.Add(Menus.Item(L("Take everything down", "Descolgar todo", "Tout décrocher"),
                                      line.Clear, enabled: line.LiveCount > 0));
            menu.Items.Add(Menus.Item(L("Open screenshots folder", "Abrir carpeta de capturas", "Ouvrir le dossier des captures"),
                                      OpenScreenshotsFolder));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menus.Item(L("Settings…", "Configuración…", "Paramètres…"), () => SettingsWindow.ShowFor(this)));
            menu.Items.Add(Menus.Item(L("How to use Pegline", "Cómo usar Pegline", "Comment utiliser Pegline"), () => TourWindow.ShowTour()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menus.Item(L("Quit Pegline", "Salir de Pegline", "Quitter Pegline"), Quit));
            return menu;
        }

        public void Quit()
        {
            Stop();
            Application.Current.Shutdown();
        }
    }
}

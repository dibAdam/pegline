<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="Pegline icon: a white card with screenshot crop marks, pegged to a line on a teal tile.">
</p>

<h1 align="center">Pegline</h1>

<p align="center">
  Screenshots, pegged to a line at the top of your screen.<br>
  Free and open source. For Windows 10 and 11.
</p>

<br>

Pegline is a Windows port of [Tendedero](https://github.com/alejandrobujan/tendedero),
the macOS app by Alejandro Buján. It keeps the original's behavior and
motion and adapts them to how Windows works. It is not affiliated with or
endorsed by Tendedero's author.

## Out of sight. Within reach.

Every screenshot you take hangs on a line just above your screen. While photos wait,
a small tab at the top of the screen shows how many. Click it, rest the pointer against
the top edge, or press the shortcut, and the line glides down. Move away and it's gone.

| | |
|:--|:--|
| Click | Copy the image. It pastes into apps as a picture and into folders as a file. |
| Press and hold | Mark it up: crop, pen, arrow, box, highlight, blur, text. |
| Hover for a moment | See it large, with a reminder of every gesture. |
| Double click | Open it in your image viewer. |
| Drag into an app | Send a copy. It stays on the line. |
| Drag into a folder | Keep it there. It leaves the line. |
| Drag to the Recycle Bin, or click the cross | Let it go. An *Undo* appears for a few seconds. |
| <kbd>Shift</kbd>&nbsp;+ drag, or right click › Pin to screen | Pin it to the screen like a fridge magnet. |
| Scroll over the photos | Walk back through older captures that fell off the end, and forward again. |
| Right click | Copy, open, mark up, pin, show in Explorer, take down. |
| Rest the pointer against the top edge, or click the tab | Bring the line down. |
| Click along the top of the screen | Put it away. |
| <kbd>Ctrl</kbd>&thinsp;<kbd>Alt</kbd>&thinsp;<kbd>T</kbd> | Show or hide the line, and take the keyboard (below). You can change it in Settings. |
| Left click the tray icon | Show or hide the line. Right click for Settings. |

Opened from the keyboard, the line can be used without the mouse:

| | |
|:--|:--|
| <kbd>←</kbd> <kbd>→</kbd> | Choose a photo; past the ends, older or newer captures |
| <kbd>Enter</kbd> | Copy |
| <kbd>E</kbd> / <kbd>O</kbd> / <kbd>P</kbd> | Mark up / Open / Pin |
| <kbd>Delete</kbd> | Let it go |
| <kbd>Ctrl</kbd>&thinsp;<kbd>Z</kbd> | Undo |
| <kbd>Esc</kbd> | Close, and return to what you were doing |

The first time Pegline starts, a three-page tour shows the essentials. It is in the tray
menu under *How to use Pegline* whenever you want it again.

Pegline never takes screenshots itself. Keep using <kbd>Win</kbd>&thinsp;<kbd>Shift</kbd>&thinsp;<kbd>S</kbd>,
<kbd>Win</kbd>&thinsp;<kbd>PrtScn</kbd>, the Snipping Tool, ShareX or anything else that saves
to your Screenshots folder. Pegline finds that folder wherever it lives, even when OneDrive
has moved it, and you can point it at another folder in Settings.

A capture flies up to the line from where you took it: a whole display, the window
under the pointer, or the region you just snipped.

## A line that's alive.

The line is a real string under tension. Every photo weighs it down a little where it
hangs, heavier photos a little more. Take one off and the line springs up and its
neighbours bounce. Now and then a gust travels down the line from one side, reaching each
photo a moment after the one before. The line never reacts to the pointer, so reaching
for a photo leaves it still.

## Stick it on the screen.

Pull a photo off the line with <kbd>Shift</kbd>&nbsp;+ drag, or choose *Pin to screen*, and it
becomes a fridge magnet: a floating copy, held by a little colored magnet, that stays above
your work without ever taking focus. Handy for copying from a reference or comparing two things.

| | |
|:--|:--|
| Drag | Move it. |
| Scroll | Resize it, around the pointer. |
| <kbd>Ctrl</kbd>&nbsp;+ scroll | Fade it, so you can see through it. |
| Drag it to the top edge | Hang it back on the line. |
| Double click | Open it. |
| The cross | Unpin it. Captures in the inbox go to the Recycle Bin. |

Pins come back where you left them after a restart, and step aside for full screen apps.

## Fresh off the press.

A capture is wet when it arrives: a cool sheen and the odd drip, drying over a minute.
Leave one hanging for a day and its corner starts to curl, a little more each day, a quiet
hint to keep it or let it go. After sunset the line becomes a string of warm fairy lights,
judged from the clock and the season alone, without location or network.

## Mark it up.

Press and hold a photo, or press <kbd>E</kbd> with it chosen, and it opens in Pegline's own
editor: crop, pen, arrow, box, highlight and blur, to hide anything that should not be
shared, plus text. Pick from seven colors and three sizes. Every step can be undone.
*Save* writes over the screenshot in its own format and the photo on the line updates;
*Copy* puts the marked-up image on the clipboard without saving. Each tool has a key:
<kbd>C</kbd> <kbd>P</kbd> <kbd>A</kbd> <kbd>R</kbd> <kbd>H</kbd> <kbd>B</kbd> <kbd>T</kbd>, sizes <kbd>1</kbd>–<kbd>3</kbd>.
*Edit in another app* is still in the right-click menu.

## Settings.

Everything is in one window, from the tray menu: the shortcut (click it and press new
keys), how many photos hang before the oldest steps back into history, the pull tab,
hover previews, bringing the line down at the top edge, the watched folder, handling
screenshots, sounds, the evening lights and opening at login.

## Your Screenshots folder. Finally clear.

Let Pegline handle your screenshots<sup>1</sup> and new captures move into its own
folder the moment they are saved. Snips you only copied to the clipboard are caught too.
Only what you keep, by dragging it out or choosing *Save to Screenshots*, ends up with your
pictures. Discarding sends a capture to the Recycle Bin, so nothing is lost for good.

## Private by design.

No account. No network. No analytics.
Pegline runs entirely on your PC, and your screenshots never leave it.

## Tech specs

| | |
|:--|:--|
| **Compatibility** | Windows 10 and 11, x64 and Arm |
| **Size** | About 300 KB, one .exe |
| **Requires** | .NET Framework 4.8, built into Windows 10 (1903 or later) and 11 |
| **Languages** | English, Spanish, French |
| **Built with** | C# and WPF |
| **Network access** | None |
| **License** | MIT |

## Install

Build it and install it for your user (no admin rights needed):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

This puts `Pegline.exe` in `%LOCALAPPDATA%\Programs\Pegline`, adds it to the Start menu and
starts it. Windows 11 tucks new tray icons into the **^** overflow; drag Pegline's onto the
taskbar to keep it in view. Turn on *Open at login* in Settings.

`scripts\uninstall.ps1` removes it again. It never deletes your screenshots.

## Build from source

You need the .NET SDK (`winget install Microsoft.DotNet.SDK.10`). Visual Studio is optional.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
.\dist\Pegline.exe
```

The SDK builds for .NET Framework 4.8 with current C#, so the result is a single small exe
that runs on any Windows 10 or 11 PC with nothing else to install.

Useful switches while developing:

| Switch | Effect |
|:--|:--|
| `--profile Name` | Keep settings, log and inbox apart (`HKCU\Software\Name`, `%LOCALAPPDATA%\Name`) |
| `--folder Path` | Watch this folder for this run only |
| `--quiet` | Skip the welcome and the first-run question |
| `--quit` | Ask the running copy to quit |
| `--gpu` | Render on the GPU instead of the CPU |
| `--preview-menu` | Open the tray menu in the middle of the screen, for screenshots |
| `--preview name` | Show one part: `settings`, `tour`, `editor`, `preview`, `keyboard`, `undo`, `tab`, `history`, or `editortest`, which draws, crops and saves the first photo |
| `--demo` | Keep the line down and pluck it and send gusts every couple of seconds |
| `--night` | Light the fairy lights whatever the time |

Launching Pegline again while it runs brings the line down.

To measure smoothness, set `PEGLINE_FRAMES=1` before starting it. The log
(`%LOCALAPPDATA%\Pegline\pegline.log`) then gets the frame rate and longest gap of every
animation, anything slow enough to cost a frame, and where the app was when it froze.
`PEGLINE_NOSLIDE=1` brings the line down inside a fixed window instead of sliding the window,
and `PEGLINE_THROTTLED=1` lets Windows' efficiency mode slow Pegline down, both for comparison.

<details>
<summary>Inside the app</summary>
<br>

| File | Role | macOS original |
|:--|:--|:--|
| `Controller.cs` | Tray, shortcut, bringing the line down and tucking it away | `AppDelegate.swift` |
| `LineWindow.cs` | The transparent strip along the top of the screen, and where each photo hangs | `LinePanel.swift`, `LineView.swift` |
| `CardView.cs` | One photo: glass frame, clip, swing, breeze, drying, curl, click, hold, drag | `PeggedView.swift`, `GrabArea.swift` |
| `Rope.cs` | The line as a string under tension, its drawing, and the fairy lights | |
| `Pins.cs` | Photos pinned to the screen | |
| `Tab.cs` | The pull tab while photos wait above | |
| `Editor.cs` | The markup editor | `Markup.swift` |
| `Preview.cs` | The large preview on hover or keyboard choice | |
| `Undo.cs` | Undo after taking down or discarding, and its pill | |
| `SettingsWindow.cs` | Settings | |
| `Tour.cs` | The first-run tour | |
| `Flight.cs` | A capture flying to the line, a card falling off, the drag preview | `CaptureFlight.swift` |
| `Line.cs` | What is hanging, and what you can do with it | `Line.swift` |
| `ScreenshotWatcher.cs` | Notices new screenshots in the Screenshots folder | `ScreenshotWatcher.swift` |
| `Inbox.cs` | Takes captures into Pegline's own folder | `Inbox.swift` |
| `MessageWindow.cs` | The global shortcut and the clipboard listener | `HotKey.swift` |
| `FullScreen.cs` | Knows when to stay hidden, and where a capture was taken | `FullScreen.swift` |
| `Shell.cs` | Opening, editing, Explorer, open at login, the folder picker | `Markup.swift` |
| `Motion.cs` | Springs and tweens that behave like SwiftUI's, and the frame clock | SwiftUI |
| `Watchdog.cs` | With `PEGLINE_FRAMES=1`, logs where the app was when it froze | |
| `Theme.cs`, `Theme.xaml` | Light and dark, accent color, Windows 11 style menus | |

The icon is drawn in code by `scripts\make-icon.ps1`.

</details>

### How it differs from the Mac version

- **The menu bar** becomes the top edge of the screen. Rest the pointer against it to bring
  the line down. A click along the top band, where title bars and tabs live, puts it away.
  Holding a mouse button (dragging a window to snap it) never brings it down.
- **Markup** becomes your image editor through the Windows *Edit* action. Saving in Paint
  updates the photo on the line.
- **Inbox mode** changes no system setting. It moves new captures out of the Screenshots
  folder instead of redirecting where Windows saves them, so there is nothing to restore.
- **Glass**: Windows can't blur behind part of a window, so the frames are a milky tint.
- **Rendering**: the line draws on the CPU, and each card is drawn once into a bitmap that
  only moves. Animation is capped at 50 frames a second, which keeps a bouncing line at
  around a quarter of one core while it moves, and at nothing when it rests.
- **New here**: the living rope, pins, drying and curling, and the fairy lights are not in
  the macOS original.
- **Where a capture was taken**: Windows doesn't record it, so Pegline matches the image
  size against the displays, the window under the pointer and the corner where a snip ended.
  When nothing matches, the capture drops in instead of flying.

<br>

---

<sub>
1. On first launch, Pegline asks whether to handle your screenshots. You can change your mind
in Settings at any time. Pegline hides automatically while an app is in full screen.
</sub>

<br>
<br>

<p align="center">
  <sub>MIT licensed. Based on <a href="https://github.com/alejandrobujan/tendedero">Tendedero</a> by
  <a href="https://alejandrobujan.com">Alejandro Buján</a>, also MIT licensed. Pegline has its own name
  and icon, as Tendedero's license asks of derived versions. See <a href="LICENSE">LICENSE</a>.</sub>
</p>

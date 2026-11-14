# SnapStudio

Screen capture tool for Windows. A single `.exe`, no installation needed (.NET Framework 4.8, already included in Windows 10/11).

> **Mac:** a native version for macOS 14+ lives in the [`mac/`](mac/README.md) folder, with the same features in the menu bar. To install it: `cd mac && ./build.sh --install --run`.

The interface is in Portuguese when Windows is set to Portuguese, and in English otherwise.

## Run
`bin\SnapStudio.exe` → the app sits next to the clock (system tray).
**Click the icon** to open the panel: capture, see recent captures, switch theme, open the history. Right-click: full menu.

## Capture
| Key | Action |
|---|---|
| **PrtScn** | Capture mode: click a window, drag a region, or **Enter** = full screen, **A** = all screens, **Esc** = cancel |
| Alt+PrtScn | Active window |
| Ctrl+PrtScn | All screens |
| Shift+PrtScn | Record screen video (start / stop) |

After a capture, the image is saved to `Pictures\SnapStudio`, **copied to the clipboard** (Ctrl+V in any app) and, depending on Settings, opened in:
- **Quick editor** (pop-up next to the clock): arrow, box, text, blur, steps, crop; *Copy and close*. Esc or clicking outside saves and closes.
- **Full editor**: all tools, properties (color, width, text size) and the annotation list.
- **Nothing**: just save and copy, with a notification.

> If another screen capture app or the Windows Snipping Tool is using the PrtScn key, SnapStudio warns you. Close that app, or turn off *Windows Settings › Accessibility › Keyboard › "Use the Print screen key to open screen capture"*.

## Record video
**Shift+PrtScn** (or the *Record screen video* button in the panel) → choose a window, a region or the full screen, as in capture mode. Recording starts after a 3-second countdown: a red frame marks the area and a floating bar shows the elapsed time, with **Pause**, **Stop** and **Cancel**. Pressing Shift+PrtScn again also stops it.

The video is saved as MP4 (H.264) in the captures folder, copied to the clipboard as a file (Ctrl+V in Teams, Outlook or a folder) and shown in the recent captures and the history. The frame and the bar do not appear in the video. It uses Windows' own encoder (Media Foundation), so there is nothing to install. Audio is not recorded yet.
In *Settings › Video*: 15/30/60 fps and whether to show the cursor.

## Editor
Arrow (A), Box (R), Text (T), **Blur** (B) and Pixelate (X) to hide areas, Highlighter (H), Step number (N), Ellipse (E), Line (L), Freehand (P), Crop (C), Select/move (V).
Ctrl+S save · Ctrl+C copy · Ctrl+Z/Ctrl+Y undo/redo · Delete removes · Shift while drawing = 45° angles/squares · Ctrl+wheel = zoom · "Drag" button to drop the image into Outlook/Teams.
Annotations stay editable when you reopen a capture.

## History
Thumbnail cards grouped by date, search (Ctrl+F), filters (Today, This week, Annotated), hover actions (copy, edit, delete), drag to other apps, F2 to rename.

## Settings
General (start with Windows, use PrtScn), Appearance (light/dark/same as Windows theme, accent color), After capture, Shortcuts, Files (folder and PNG/JPG format), Video (fps, cursor).

## Build
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
No .NET SDK needed: it uses the `csc.exe` from the .NET Framework that ships with Windows. Source code in `src\`. User data in `%APPDATA%\SnapStudio`.

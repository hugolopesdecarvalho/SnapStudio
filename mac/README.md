# SnapStudio for macOS

Native Mac version of SnapStudio (Swift, AppKit and SwiftUI). It has the same features as the Windows version and lives in the **menu bar**. Requires macOS 14 (Sonoma) or later and runs natively on Apple Silicon and Intel.

The interface is in Portuguese when macOS is set to Portuguese, and in English otherwise.

## Install from a release
1. Download **SnapStudio-macOS.zip** from the repository's *Releases* page and open it. Finder extracts `SnapStudio.app`.
2. Drag `SnapStudio.app` to the **Applications** folder and open it.
3. The app is not notarized by Apple, so macOS blocks it the first time. Go to *System Settings › Privacy & Security*, scroll down to the message about SnapStudio and click **Open Anyway**. You only need to do this once.

## Menu bar
SnapStudio appears in the menu bar (viewfinder icon).
**Click** the icon to open the panel: capture, record video, see recent captures, switch theme and open the history. **Right-click** (or Control-click) for the full menu.

### Screen recording permission
On the first capture, macOS asks for permission. Turn on SnapStudio in
*System Settings › Privacy & Security › Screen & System Audio Recording* and reopen the app.
SnapStudio's *Settings › General* shows whether the permission has been granted.

> Since 1.1.2, the app is signed with a fixed identity (`com.snapstudio.mac`), so the permission is kept when you update or rebuild. If you are coming from an earlier version and the app keeps asking for permission even though it is turned on, remove the old entry from that list with “−” and allow it again (only once).

## Capture
Macs have no PrtScn key. The default shortcuts avoid the system ones (⌘⇧3/4/5, which keep working) and can be changed in *Settings › Shortcuts*:

| Key | Action |
|---|---|
| **⌃⇧4** | Capture mode: click a window, drag a region, or **↩** = full screen, **A** = all screens, **Esc** = cancel |
| ⌃⇧6 | Active window |
| ⌃⇧3 | All screens |
| ⌃⇧5 | Record screen video (start / stop) |

After a capture, the image is saved to `Pictures/SnapStudio`, **copied to the clipboard** (⌘V in any app) and, depending on Settings, opened in:
- **Quick editor** (pop-up below the menu bar): select, arrow, box, text, blur, steps, crop; *Copy and close* (⌘↩). Esc or clicking outside saves and closes. Only used right after a capture: captures opened from the panel, history or a notification open in the full editor.
- **Full editor**: all tools, properties (color, width, text size) and the annotation list.
- **Nothing**: just save and copy, with a notification. Clicking the notification opens the editor.

## Record video
**⌃⇧5** (or *Record screen video* in the panel) → choose a window, a region or the full screen. Recording starts after a 3-second countdown: a red frame marks the area and a floating bar shows the elapsed time, with **Pause**, **Stop** and **Cancel**. The time also appears in the menu bar. Pressing ⌃⇧5 again stops the recording.
The video is saved as MP4 (H.264) and copied as a file (⌘V in Finder, Mail, Teams…). The frame and the bar do not appear in the video. Audio is not recorded yet.

## Editor
Select (V), Arrow (A), Box (R), Text (T), **Blur** (B) and Pixelate (X) to hide areas, Highlighter (H), Step number (N), Ellipse (E), Line (L), Freehand (P), Crop (C).
⌘S save · ⇧⌘S save as · ⌘C copy · ⌘Z/⇧⌘Z undo/redo · ⌫ deletes · arrow keys move (⇧ = 10 px) · ⇧ while drawing = 45° angles/squares · pinch or ⌘+wheel = zoom · ⌘9 fit · ⌘0 actual size · ⌘← / ⌘→ (or ‹ › in the header) previous / next capture in the folder, saving changes first · *Drag* button to drop the image into another app.
After placing an annotation the editor returns to Select (V) with it selected; numbered steps and freehand stay active. The full editor's properties panel shows only what applies to the selected annotation or active tool: color, width, text size, text *Outline* (auto/white/black/none), *Fill* with opacity (boxes, ellipses), *Rounded corners* with a radius (boxes, highlighter, blur, pixelate) and *Rounded tip* (arrows).
Annotations stay editable when you reopen a capture (helper files in `~/Library/Application Support/SnapStudio/edit`).

## History
Thumbnail cards (images and videos) grouped by date, search (⌘F), filters (Today, This week, Annotated, Videos), hover actions (copy, edit, show in Finder, Trash), drag to other apps, context menu with *Rename…*. ⌫ moves the selected capture to the Trash.

## Settings
A sidebar with General (open at login, permission status), Appearance (light/dark/same as macOS theme, accent color), After capture (what opens, clipboard, notifications), Shortcuts, Files (folder and PNG/JPG format), Video (15/30/60 fps, cursor) and About (version and updates). Changes apply immediately.
User data in `~/Library/Application Support/SnapStudio`.

## Updates
SnapStudio checks the [releases page](https://github.com/hugolopesdecarvalho/SnapStudio/releases) shortly after launch and then once a day. When a newer version is published it shows a notification, a banner in the panel and a window with the release notes, with **Download**, **Later** and **Skip This Version**. You can also use *Check for Updates…* in the menu bar icon's right-click menu or in *Settings › About*, where automatic checks can be turned off. Only GitHub's public API is contacted; no personal data is sent.

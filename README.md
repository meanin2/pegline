# Pegline 0.2.1 beta — Windows screenshot clothesline

**Windows beta available:** application compiled; **113 tests passed**, six WPF views rendered and inspected, native shelf lifecycle checks passed. Full interactive desktop acceptance remains pending. See [verification](VERIFICATION.md), [local code review](docs/LOCAL_REVIEW.md), and the [release/download page](https://github.com/meanin2/pegline/releases/tag/v0.2.1-beta.1).

[Download portable Windows beta](https://github.com/meanin2/pegline/releases/tag/v0.2.1-beta.1) · [Windows CI](https://github.com/meanin2/pegline/actions/workflows/windows.yml)

Extract the portable ZIP and run `Pegline.exe` with its `.exe.config` beside it. The unsigned application targets Windows 11 x64 with .NET Framework 4.8/4.8.1. No installer or administrator rights required.

Pegline recreates Tendedero's screenshot clothesline workflow for Windows under a new name and original icon. It implements its own screenshot capture and image editor rather than substituting Paint. It is not affiliated with or endorsed by Tendedero's author.

## Build and start on Windows

1. Extract **the entire ZIP** into a regular local folder, for example `Documents\Pegline`. Do not run a script from inside the ZIP viewer. Keep this folder in a stable location if enabling startup later.
2. Double-click **`Build-and-run.cmd`**. No administrator rights should be needed.
3. The script looks for the local .NET Framework compiler, compiles the application and test executable, runs the tests, and launches the application **only if every step succeeds**.

Initial target: **Windows 11 x64**, .NET Framework **4.8 or 4.8.1**, WPF. The direct build uses the C# 5 compiler in the Windows .NET Framework folder. No NuGet, npm, Electron, browser engine, Python, or downloading compiler/bootstrap scripts is involved in the Windows build. Some stripped-down or managed Windows installations may not provide the expected compiler/assemblies; the script then stops with an explanation rather than installing anything. A conventional Visual Studio project is also included.

Expected generated files after a successful Windows build:

```text
bin\Pegline.exe
bin\Pegline.exe.config
bin\Pegline.Tests.exe
bin\build.log
bin\test-results.txt
```

For subsequent launches, run `bin\Pegline.exe`. Copying a successful build to another PC requires its `.exe.config` alongside the `.exe` and a suitable Windows .NET Framework installation. The source archive itself is not an installer. See [building details](docs/BUILDING.md).

## What changed in 0.2

The review fixes shared annotation IDs, incorrect undo/save-state tracking, destructive crop flattening, clipboard sequence races, all-zero-alpha DIB handling, mirrored EXIF orientation, watcher shutdown races, and resource cleanup. It adds a searchable local screenshot library, repeat-region capture, pixel loupe, keyboard precision, base-image comparison, direct edited-image copying, and a revised click-through overlay composition. The full [review/change record](docs/REVIEW_CHANGES.md) distinguishes source changes from unexecuted tests.

**Optional Windows verification:** run `Verify-Windows.cmd` to compile, run the authored tests, and render the actual WPF shelf/editor/library in light and dark appearances. The harness uses synthetic images and an isolated temporary data folder, not real screenshots or clipboard contents. Its six PNGs and log are generated **only when that script successfully runs on Windows**; six were generated and inspected in the local Windows validation.

## Controls implemented in the source

| Gesture or shortcut | Intended behavior |
| --- | --- |
| `Ctrl+Alt+4` | Capture a rectangular region; Escape cancels. |
| `Ctrl+Alt+3` | Capture the monitor under the pointer. |
| `Ctrl+Alt+5` | Open the capture chooser: region, window, monitor, all monitors, freehand. |
| `Ctrl+Alt+T` | Show/hide the line. |
| `Ctrl+Alt+R` | Repeat the last region while the monitor layout remains unchanged. |
| `Ctrl+Alt+L` | Open the searchable local screenshot library. |
| Rest at a monitor's top edge | Reveal the line on that monitor after a short dwell. |
| Move away | Retract the line, except while interacting or during its brief new-capture preview. |
| Click a card | Copy the full-resolution image and file reference. |
| Double-click a card | Open the image in the Windows default application. |
| Hold a card for 0.45 seconds | Open Pegline's own Markup editor. |
| Drag a card | Offer an actual Windows Shell file transfer, not just an in-app preview. |
| Release an unaccepted drag | Animate back to the shelf. |
| Hover and click the corner cross | Take down an external file; recycle an app-owned inbox capture. |
| Right-click a card | Copy, open, Markup, reveal in Explorer, and context-appropriate save/discard actions. |
| Tray icon | Toggle the line; right-click for captures, folders, settings, startup, sound, and exit. |

Shortcuts are configurable. Registration failure is reported instead of overriding another app. Pegline does **not** intercept or replace `Win+Shift+S`, change Snipping Tool settings, or silently redirect Windows' screenshot folder.

## What has been coded

**The clothesline:** original-reference layout dimensions, parabolic rope, metallic clips, translucent rounded frames, preserved aspect ratios, random tilt, spring arrival, wind nudges, hover/press/copy feedback, animated show/hide, origin-to-card flight, screen-height falls, screen-width-dependent capacity, persistence, and missing-file removal. The overlay separates decorative rope/shadows/clips into non-interactive windows; the interactive window paints only cards. No-activation and per-pixel click-through behavior still requires native validation. Fullscreen detection, monitor selection, and following the active virtual desktop are implemented but need real desktop testing.

**Capture:** built-in rectangular, window, monitor, combined-desktop, and freehand capture; repeat last region with a monitor-layout guard; pixel loupe (`L` toggles it), arrow-key cursor nudges (Shift for 10 pixels), delay; optional cursor; exact physical capture bounds for the app's own flight animation. The regular Windows screenshot directory and extra user-chosen directories can be watched. Snipping Tool clipboard images can be collected; collecting arbitrary clipboard images is separately opt-in. Opposite clipboard/folder arrivals are content-correlated so a dual-delivered screenshot can appear once while same-channel repeated images remain distinct.

**Markup:** pen, heuristic sketch recognition, highlighter, annotation eraser, rectangles, ellipses, lines, arrows/double arrows, hexagons, stars, speech bubbles, text styles/alignment, spotlight, magnifier, crop, rotate, flip, opaque redaction, inserted images, and mouse/pen signature input with optional local signature storage. Annotations can be selected, moved, resized, reordered, duplicated, and undone/redone. Save writes full-resolution output back into the shelf; Save As is available, and external modifications trigger conflict protection.

**Library:** up to 200 recent file references, filename/folder search, thumbnail previews, re-hanging, direct editing/copying, Explorer reveal, importing existing images and reference-only forgetting. It does not index image contents, perform OCR, move existing files, or create full duplicate image archives. Missing/moved files remain visible as unavailable references. Pausing collection stops automatic folder/clipboard intake without disabling explicit capture.

**Editor polish:** keyboard tool shortcuts, cursor-anchored zoom, actual-size/fit commands, one-pixel annotation nudges, precise line/square constraints, editable layers retained through crop, revision-aware undo, cancellable gestures, grouped slider edits, layer hit-testing, dirty-state titles and improved light/dark/high-contrast control styling. “Base image” hides annotations temporarily but retains the current crop/rotation. `Ctrl+C`/Copy result copies flattened edited pixels without attaching the original source path. Saved files remain flattened; this is not a persistent layered-project format.

These are descriptions of the **source implementation**, not claims that the Windows behaviors have already been observed. [Feature matrix and adaptations](docs/FEATURE_PARITY.md).

## File handling and privacy

- An **external screenshot** remains at its original path. Taking it down does not delete it. Explicit Recycle Bin actions and confirmed file moves are separate operations.
- A **Pegline-owned inbox capture** can be recycled using Discard or kept by moving it to a folder/Desktop.
- **Clear the line and capacity overflow remove cards only.** They do not erase screenshot files. There is no timed background purge.
- Overwriting through the editor makes a local original backup first and uses a same-directory atomic replacement. Some filesystems or locked files can reject the operation; the app does not delete the original as a fallback.
- Session paths, recent-file references, preferences, owned images, and optional saved signatures live under `%LOCALAPPDATA%\Pegline`. The `Recovery` subfolder holds edit backups. Backups may contain the original, unredacted pixels, so manage them accordingly.
- No application telemetry, analytics, accounts, web service, remote configuration, crash upload, update checking, or automatic updater is implemented. The build does not download dependencies. Compiler/test logs stay local.
- Copying an image requests exclusion from Windows Clipboard History and Cloud Clipboard upload. This is not a guarantee about Windows settings, third-party clipboard managers, OneDrive-synced folders, or applications you choose to open/share images with.

## Explicit limits before claiming parity

The new three-window shelf composition and virtual accessibility peers have not been exercised on Windows. The frames use a custom translucent approximation, not Apple's live blur/material implementation. External capture files do not reliably provide their screen origin, so only Pegline's own captures have authoritative flight bounds. WebP/HEIF decoding depends on installed Windows codecs; unsupported or multi-frame input formats are saved as a supported separate image when edited. The capture engine reads the visible desktop with GDI; protected content, secure desktops, offscreen window content, and HDR fidelity are not promised. Sketch recognition is heuristic, and signatures use local input rather than Apple's device/camera integrations.

Most importantly, compilation and Windows acceptance remain outstanding. Mixed DPI, fullscreen detection, cross-desktop behavior, Explorer/Recycle Bin transfer semantics, browser/chat drop targets, and non-activation/click-through behavior are explicit acceptance tests, not assumed successes.

## Development files

`src/` contains 16 application C# files, `tests/TestProgram.cs` contains 111 authored test cases, and `tools/static_audit.py` is an **optional maintainer-only** Python standard-library source/configuration check. Python is not required to build or run Pegline on Windows. No C# tests have been run in the delivery environment.

Start the Windows validation with disposable screenshots rather than irreplaceable files. The detailed checklist is [WINDOWS_ACCEPTANCE.md](docs/WINDOWS_ACCEPTANCE.md).

## Attribution

Inspired by Tendedero by Alejandro Buján, with Windows Shell implementation reference from Cordel by Samuel. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The Tendedero name, original icon, documentation art, Apple sound assets, and Cordel's Opirex branding are not redistributed in this package. Pegline has its own icon and synthesized sounds.

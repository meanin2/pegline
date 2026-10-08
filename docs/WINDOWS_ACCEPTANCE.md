# Windows desktop acceptance

Use disposable test screenshots and ordinary, non-elevated applications first. Record Windows version/build, Framework version, monitor sizes/scales/positions, screenshot-tool version, app version, and the current source hash. Do not mark a test passed based on source inspection or a compile alone.

## Recorded evidence — October 8, 2026

Compilation, 113 regressions, six synthetic WPF views and native shelf window lifecycle passed on Windows 11 25H2 build 26200.9457, x64, CLR 4.0.30319.42000. `IntegrationSmoke` additionally passed 13 checks with synthetic files in an isolated store: watcher baseline/new/change/delete, library tracking/re-hang/forget, manual import, external discard, session restore, clear, and file preservation. These call real controller paths, not pointer gestures. Unmarked cases below remain NOT RUN; do not infer whole-row acceptance from partial coverage.

## 1. Build, launch, lifecycle

Run `Build-and-run.cmd` from a path containing spaces and then a non-ASCII path. Save `bin/build.log` and `bin/test-results.txt`. All 113 C# cases must execute with zero failures before app launch. Verify the tray icon, first-use hint, no taskbar/Alt-Tab shelf entry, quit, relaunch persistence, and second launch toggling the existing instance. Observe that canceling an unsaved editor close prevents a normal Quit from discarding the session's edits.

Opt into startup, sign out/in, and verify the exact executable starts once. Opt out and confirm the Run entry is removed. Do not move a startup-enabled build without disabling startup first.

## 2. Source and ownership matrix

| Operation | Required observation | Result |
| --- | --- | --- |
| Built-in capture, private inbox on | File lands in Inbox and one card appears. | NOT RUN |
| Built-in capture, private inbox off | File lands in standard screenshots directory, is external, and one card appears. | NOT RUN |
| Win+PrintScreen | Saved screenshot appears once. | NOT RUN |
| Win+Shift+S, auto-save on | Clipboard/file dual delivery normally yields one card. | NOT RUN |
| Win+Shift+S, auto-save off | Clipboard capture appears once when snip collection is enabled. | NOT RUN |
| Arbitrary image copied in another app | Not collected by default; collected only with the explicit setting. | NOT RUN |
| Pegline click-to-copy | Does not create a second card through its own clipboard listener. | NOT RUN |
| Two deliberate identical screenshots quickly | Same-channel repeats are not lost; test interleaved dual-delivery events too. | NOT RUN |
| Cross on external file | Card disappears, original file still exists unchanged. | NOT RUN |
| Cross on owned inbox capture | Goes to Recycle Bin; restore through Explorer works. | NOT RUN |
| Clear line / capacity overflow | Cards disappear but external and inbox files remain. | NOT RUN |
| App-owned path through a junction/symlink | Automatic discard does not treat unrelated storage as owned. | NOT RUN |
| Slow/large file creation, temporary rename | No partially decoded card; retries eventually import valid finished image. | NOT RUN |
| File changed/deleted/moved outside app | Thumbnail refreshes or card is pruned without unrelated deletion. | NOT RUN |
| Folder disappears then returns | No fatal crash; recovery scanning resumes. | NOT RUN |

Repeat watch tests with known-folder OneDrive redirection, a custom capture directory, Unicode names, and disabled folder permissions. OS/cloud activity is external to the app and must not be confused with app telemetry.

## 3. Overlay and timing

Put a text editor in the foreground and type while the shelf reveals. Keyboard focus must remain in the editor. Click underlying browser tabs, close buttons, title bars and content through spaces between cards. Hover a card and confirm it remains clickable. Right-click should not leave the whole strip blocking underlying windows after the menu closes.

Check 250 ms dwell, 500 ms leave delay, 2.5 s new-image peek, 450 ms hold, shortcut pin until visited, top-edge click suppression, drag hold, long-press/drag/click mutual exclusion, and double-click opening without leaving a stuck press state. Verify empty hint and the original geometry, aspect ratio, frame/clip alignment, copy pill, tilt, breeze, flight handoff, and full-height fall. Compare actual rendering with the original source/reference app; no visual acceptance has occurred yet.

## 4. Multiple displays and desktops

Test 100%/150%, 100%/200%, and identical scaling; negative X, negative Y, vertical stacking, secondary primary-display assignment, portrait monitor, attach/detach, sleep/wake, and scale changes. The shelf should follow the chosen monitor, remain sized in DIPs, and avoid stale/out-of-bounds hit targets. Capture across a monitor boundary and inspect flight endpoints.

Switch virtual desktops with keyboard and Task View, including an empty desktop and one where no ordinary window owns focus. Verify public desktop-follow integration actually works; failure here remains a parity blocker, not a reason to mark “all desktops” passed.

Test browser fullscreen video, F11 browser, slideshow, borderless game, exclusive fullscreen where applicable, and a fullscreen app on a nonforeground display. The shelf should stay out of the way and return afterward. Ensure an ordinary maximized window does not incorrectly suppress it. Fullscreen detection is heuristic and needs these real observations.

## 5. Shell drag-and-drop

| Destination / case | Required outcome | Result |
| --- | --- | --- |
| Same-drive Explorer folder | Confirmed move preserves exact bytes at target; card leaves after completion. | NOT RUN |
| Different local drive | Confirmed copy/move semantics; no double deletion or silent missing destination. | NOT RUN |
| Desktop folder | Same rules, including redirected Desktop. | NOT RUN |
| Desktop Recycle Bin | File recoverable in Recycle Bin; card removed only after success. | NOT RUN |
| Browser upload, chat attachment, editor | Destination receives usable original file/image, source remains when copy is chosen. | NOT RUN |
| Ctrl-copy, Shift-move where target supports them | Respect actual destination completion; no false cleanup. | NOT RUN |
| Escape / unsupported desktop spot | No source deletion; card returns. | NOT RUN |
| Delayed file consumption | Destination finishes reading without source loss. | NOT RUN |
| Permission denied / overwrite canceled | Error is visible; source remains unless the destination genuinely completed the move. | NOT RUN |
| Elevated target / privilege mismatch | Windows restrictions handled without bypass or app-wide elevation. | NOT RUN |
| Network/removable explicit discard | Safe refusal or Windows-managed outcome; no silent permanent delete. | NOT RUN |

Test optimized and unoptimized Shell moves. A drag-return effect alone is insufficient evidence of a successful transfer; inspect both bytes and completion formats where diagnosing failures.

## 6. Capture backend

Test rectangular and square-constrained regions, window selection, full current monitor, all monitors, freehand transparency, 0/3/5/10 second delay, optional cursor position/hotspot, cancellation through Escape/right-click, minimum-size rejection, and selecting around screen edges. Move pointer to a second monitor before a delayed monitor capture to establish intended monitor selection timing.

Check that Pegline's shelf/flight/countdown is absent from its own captured pixels. Verify physical pixel dimensions and exact recorded region used for the flight. External captures should drop in when no bounds are available, not pretend to have an exact origin. Test protected content and HDR only to document limitations, not to promise unsupported capture behavior.

## 7. Markup

Exercise every tool, both text directions, font controls, stroke/fill and width settings, move/resize, Shift aspect lock, duplicate/reorder/delete, selection after undo/redo, crop undo, rotate/flip undo, zoom/pan, and canceling an in-progress gesture. Verify signature creation, optional persistence, use, and forgetting locally. Test inserted transparent images and actual pen hardware where available.

Save a PNG with transparency and compare dimensions/pixels. Save JPEG to confirm intentional white alpha compositing. For GIF/TIFF/multipage/HEIF/WebP source, verify the app does not silently overwrite an unsupported format with mislabeled PNG data. Read-only/locked output must fail without deleting the original. Change the source externally while the editor is open; confirm conflict handling preserves both versions. Inspect the Recovery copy, and verify users understand pre-redaction pixels persist there.

Opaque redaction must be solid in the exported image. Do not distribute the original/Recovery file when the intent is to share a redacted screenshot.

## 8. Privacy and endurance

Observe process network activity: no application-originated web requests are expected. External viewers, OneDrive, clipboard services, and OS verification are separate processes/features. Confirm general clipboard collection and startup remain opt-in, and no updater/service/scheduled task is installed.

Exercise repeated captures, 100 copy/drags, repeated editor open/close, repeated falls and monitor switches. Measure memory, GDI handles, idle CPU, rendering load, and response latency. A static source audit does not establish resource stability or an idle-CPU target. Retain manual notes and actual measurements with the tested source version.


## 9. Review regression acceptance (0.2; NOT RUN)

Run `Verify-Windows.cmd`, inspect all six real-WPF renders, and keep its logs with the source revision. Check readable labels, contrast, quick tools, sidebar scrolling, light/dark controls and library rows. The harness must not touch the normal Pegline data folder or clipboard.

For the shelf's new back/card/front HWND stack: test a rapid first click on a card without pausing first, clicks on the rope/clip/shadow/copied-pill regions reaching the underlying app, no taskbar/Alt-Tab debris, no focus stealing with Windows active-window tracking, coordinated hide/fullscreen/DPI/desktop transitions, and eventual cleanup of all windows after exit. Inspect animation clipping and exact card/clip alignment. Confirm that a screen reader sees separate live card identities and that Invoke copies the intended card; use the library for keyboard file actions.

For the editor: draw at least three different marks, select/edit/delete each independently, duplicate one, cancel a move with Escape, cancel another by losing focus, undo to the loaded image, save, undo away from save, redo back to save, and branch after undo. Dirty state and redo availability must be correct throughout. Crop after annotations and continue editing those same layers. Test Shift line snapping and square shapes, cursor-anchored zoom, Ctrl+0/Ctrl+1, arrow/Shift-arrow nudges, and temporary base-image preview. Crop/rotation is intentionally retained in that preview. Rotate/flip are still flattened but undoable.

For file safety: open two different screenshots in editors, try Save As from one into the other, and confirm refusal without losing either session. Then save while another process changes the file, choose a new filename that is created by another process before writing, force a backup failure, and try locked destinations. No silent overwrite/delete fallback is allowed. Copy result from an unsaved redacted image into a browser and a chat app; neither may attach the unedited original. Inspect fractional redaction edge pixels in an exported full-resolution PNG. Original data remains in recovery backups.

For intake: produce repeated clipboard updates, image replacements during delayed clipboard rendering, paired folder/clipboard captures, legitimate identical repeated captures, all-zero-alpha RGB DIBs, partially transparent PNGs, large slow image writes, watched-folder deletion/recreation, pause/resume, and shutdown while imports are pending. Pause/resume must not revive a stale generation. Test EXIF orientations 1–8 with asymmetric patterns. Monitor decode concurrency and handle counts during bursts.

For the library: populate more than one shelf's capacity, re-hang an older entry, search by path/name, import external images, edit and save one, forget a reference, move/remove a file externally, relaunch, and exceed 200 references with synthetic data. Forgetting and capacity trimming must never delete the corresponding files. The reference list is not a persistent layered-project archive or OCR index.

For precise capture: toggle the loupe with L, select near all screen edges, move the cursor 1/10 pixels with arrows/Shift, repeat a region, then change monitor placement or resolution and confirm repeat is rejected rather than capturing stale coordinates. A scale-only change that leaves physical monitor bounds unchanged retains the physical-pixel region; verify its dimensions and placement rather than expecting invalidation. The fingerprint includes monitor names and physical bounds, not DPI. Test both ordinary and high-DPI displays.

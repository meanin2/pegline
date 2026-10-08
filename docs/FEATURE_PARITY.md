# Feature verification and platform differences

Reference: Tendedero `d99145477f1b86c0d11d205868ab507a57a54980` (pinned source in the preceding review). Windows implementation reference: Cordel `10f38049eee84032c9c0cd11a681a1701b511b93`.

**Current evidence:** Windows compilation and 113 regressions passed; six WPF views rendered and inspected; native shelf lifecycle and 13 isolated controller integration checks passed. Entries marked source-present are not automatically desktop-verified. Full interactive acceptance remains pending.

Upstream reviewed October 8, 2026 at `ed0618a67cc59e0a8621b97bb5e32ab9af3b0b69`, seven commits beyond the pinned reference. Changes include translation resources/Simplified Chinese, documentation, and macOS menu-band edge containment. Pegline currently provides English/Spanish; latest upstream language coverage differs. [Exact upstream comparison](https://github.com/alejandrobujan/tendedero/compare/d99145477f1b86c0d11d205868ab507a57a54980...ed0618a67cc59e0a8621b97bb5e32ab9af3b0b69).

## Clothesline and interaction

| Original behavior | Pegline implementation | Verification / qualification |
| --- | --- | --- |
| Hidden top-of-screen clothesline with dwell reveal | `VisibilityState`, `Controller.Tick`, `ShelfWindow` | Source present; native hover/edge behavior untested. |
| Reveal on the monitor being used | Pointer/source-bounds monitor selection | Source present; mixed-DPI transitions untested. |
| Clicking top/menu region suppresses reveal until leaving | Top-edge button state suppression | Windows title bars/tabs replace macOS menu-bar affordance; acceptance needed. |
| New image peeks for 2.5 seconds | Explicit arrival state independent of item count | Source present, including full shelf arrival. |
| Hide after pointer leaves for 0.5 seconds | Visibility state with interaction guards | Source present. |
| Shortcut-opened shelf stays until visited or toggled | Pinned state | Source present. |
| 210-point strip, 174-point spacing, maximum 150-point card width | Original numerical geometry reproduced in `ShelfLayout` | Geometry regression cases passed. |
| Curved rope, sag dependent on width | Same parabolic sag relationship | Light/dark WPF output inspected; side-by-side Mac interaction comparison pending. |
| Clip, frame, rounded corners, aspect ratio | Custom WPF drawing; generated metallic clip | Frame is a translucent gradient approximation, not true live Apple backdrop material. |
| Random tilt, arrival spring, wind, hover/press/copy feedback | Analytic damped springs and per-card state | Native animation and high-refresh smoothness not measured. |
| Capture flies from the captured region to the line | Built-in capture returns exact physical ROI; layered-window flight | Implemented for Pegline capture. External image origin unknown → drop-in instead of invented coordinates. |
| Discard falls down outside the shelf's bounds | Separate layered moving flight window | Source present; rendering untested. |
| Cancelled drag flies back | Return-flight after unaccepted drop | Source present; Shell drag feedback interaction untested. |
| Non-activating floating strip | Win32 no-activate/tool styles, WPF ShowActivated false, WM_MOUSEACTIVATE | Must verify no focus theft across ordinary apps. |
| Clicks between cards pass through | Card-only interactive transparent window + always-click-through back/front decoration windows | Must verify with underlying tabs/title bars/buttons; the three-window design is new in 0.2 and must be validated natively. |
| Fullscreen suppression | Per-monitor visible-window/full-bounds heuristic | Adapted to Windows; no equivalent of Apple's fullscreen Space type. Must test games, videos, presentations. |
| Available across desktops | Public IVirtualDesktopManager follows the active foreground desktop | Implemented; not universal shell pinning. Empty desktops/focus races are acceptance cases. |
| Capacity follows screen width, oldest falls off | Clamp 3–12 using original spacing relationship | Source present; dropped cards do not erase files. |
| Empty hint and first-launch hint | Shelf hint and welcome balloon | Source present; texts English/Spanish. |

## Capture and file actions

| Original behavior | Pegline implementation | Verification / qualification |
| --- | --- | --- |
| Watch screenshot files without being the capture engine | Windows known screenshots directory + optional directories + stable file watcher | Standard Win+PrintScreen and auto-saved Snipping Tool paths need Windows testing. |
| Use regular capture workflows | Source-aware Snipping Tool clipboard listener | Does not override OS shortcuts. Snipping Tool process/version variations need validation. |
| App-owned capture inbox | Own capture writes into `%LOCALAPPDATA%\Pegline\Inbox` | Same privacy/storage outcome for Pegline captures, not a system setting takeover. |
| Disable native floating thumbnail/delay while handling captures | Own capture has no native thumbnail notification to disable | Deliberate platform substitution. Snipping Tool notifications/settings remain unchanged. |
| Restore system screenshot settings on exit | No system screenshot settings are changed | No restoration is needed; existing workflows remain intact. |
| Screenshot selection/capture supplied by OS | Custom region, window, monitor, all-monitor, freehand, delay, optional cursor | Additional implementation replaces the missing portable capture backend. Visible-desktop GDI capture only. |
| Single-click copies image + file reference | PNG + bitmap + file-drop formats, clipboard retry and own-copy marker | Needs receiving-app testing. |
| Double-click opens system viewer | Shell open uses registered default image app | Native execution not tested. |
| Drag to app copies; card remains | Shell IDataObject, copy/move offer, drag-image helper | Destination determines effect; exact targets need testing. |
| Drag to folder saves there; card leaves | Shell transfer with preferred move and conservative completion | Cross-volume and optimized/unoptimized transfers are explicit acceptance tests. |
| Drag to Trash | Shell data object supports normal Recycle Bin target | Must test desktop Recycle Bin with local disposable files. |
| Failed/cancelled drag leaves file intact | None-effect return animation; no source deletion | Source present; native behavior unverified. |
| Explicit discard of app-owned screenshot | Recycle Bin operation for a verified owned inbox location | Junction/reparse-point boundary guard; no silent permanent-delete fallback. Windows may show its own confirmation. |
| Cross on external screenshot means take down, not delete | Owned versus external policy is explicit | Source present; test external files remain. |
| Save app-owned capture to Desktop | Unique path, move or verified cross-drive copy plus source recycling | Destination conflicts/cancel behavior need native testing. |
| Clear all / overflow removes cards, not files | Drops card state only; no timed disk cleanup | Matches original code, rather than inferring file deletion from promotional wording. |
| Restore hanging images | Session JSON with path, ownership and tilt | Missing entries are skipped; corruption falls back. |
| Update thumbnails and prune moved/deleted files | File watcher plus periodic state reconciliation | Stable decode retry; must test large/slow writer and rename patterns. |
| PNG/JPEG/HEIC/TIFF/GIF/WebP files | Windows WIC decoding, plus BMP | HEIF/WebP require locally installed compatible codecs; animated/multipage imagery is represented by its first frame. |

## Integrated Markup replacement

Tendedero delegates editing to Apple's system Markup service, whose toolset can vary by OS. Pegline implements its own editor rather than claiming it can invoke Apple APIs on Windows.

| Editing capability | Implementation |
| --- | --- |
| Pen and pressure-aware ink | Mouse/stylus input, scalable stored stroke points. |
| Sketch recognition | Undoable line/ellipse/rectangle heuristic; not Apple's recognizer. |
| Highlighter | Translucent freehand annotation. |
| Erase | Removes annotations, not arbitrary original pixels. |
| Shapes and arrows | Rectangle, ellipse, line, arrow/double-arrow, hexagon, star, speech bubble. |
| Style controls | Stroke/fill colors, fill toggle, dashed outline, thickness; fonts, sizes, bold, italic, alignment. |
| Text | Add and edit; rendering chooses RTL flow for Hebrew/Arabic-range text. Mixed-direction typography needs actual input testing. |
| Signature | Local mouse/pen InkCanvas and optional locally stored signatures; inserted images can also be used. No Apple Continuity or camera-service integration. |
| Spotlight and magnification | Masked focus area and adjustable magnifier. |
| Selection and layout | Move, resize, preserve aspect ratio with Shift, reorder, duplicate, delete. |
| Crop / rotate / flip | Crop retains editable layers and IDs. Rotate/flip flatten the current composition, with undo history. |
| Undo/redo | Bounded snapshots, stable layer IDs, saved revisions and cancellable gesture transactions. |
| Save / Done | Full-resolution render, format-aware encode, single-read load/hash, expected-hash checked writes, local backup, atomic/no-clobber writes, shelf refresh. |
| Save As | Native save dialog; required when preserving unsupported/multipage source formats. |
| Opaque redaction | Solid-color overwrite in exported image. Backups retain original pixels; not a promise of secure erasure. |
| Insert another image | Local file chooser and codec-based image decode. |

Image annotations are editable during the editor session. Saved output is flattened, as a normal edited screenshot; an indefinitely persistent layered project format is not implemented or claimed.

## App lifecycle / release

English/Spanish menus, appearance preference, sound toggle, synthetic sounds, startup opt-in, single-instance behavior, local state, and settings are implemented. No networking client, account, updater, or telemetry is present. The icon is original. Windows code signing, an installer, a Store listing, automatic update distribution, and a completed accessibility audit are not part of this source delivery.

## 0.2 additions beyond the original shelf

Local 200-reference library with search/import/edit/copy/re-hang/forget; repeat-region capture guarded by monitor topology; pixel loupe and cursor nudges; paused collection and quiet arrivals; base-image comparison and copy-edited-result; keyboard tool/zoom/nudge controls; virtual shelf automation peers. Library collection, re-hang, forget, import, persistence and image refresh/prune passed isolated native integration checks. Other additions remain source-present without full desktop acceptance. Reference-style dimensions and screen-width-dependent shelf capacity are retained.

New source details and regression cases are documented in `REVIEW_CHANGES.md`. The real-WPF render harness ran successfully on Windows.

## Release gates still open

1. Completed: compile and execute 113 regression tests, six WPF renders, shelf lifecycle, and 13 isolated controller integration checks.
2. Run the native acceptance checklist with real Shell/clipboard targets and displays.
3. Compare the actual rendering and interaction feel side by side with the original reference.
4. Resolve any remaining feature, material, codec, and integration differences before claiming those specific behaviors verified.

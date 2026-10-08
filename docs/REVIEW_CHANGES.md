# Pegline 0.2 — review and implementation changes

Date: October 8, 2026. Baseline: the supplied `Pegline-Windows-source.zip` (0.1). Status: **changed in source and audited locally; not compiled or run on Windows**. No measured reliability, performance or parity claims are made.

## Correctness fixes

| Finding in the supplied source | Revision | Regression evidence authored |
| --- | --- | --- |
| `Mark.Clone()` preserved the ID and was used for every new annotation derived from a style object. | `Mark.NewCopy()` creates new IDs; history Clone keeps existing IDs. New strokes, text, inserted images, signatures and duplicates use the correct operation. | Independent identities, preserved history identity, independent points. |
| Dirty was a boolean rather than a saved revision; undoing to the saved image still looked dirty. | Monotonic revisions and a saved revision, kept through snapshots. | Undo-to-original, redo-to-save, branched edit after undo. |
| Canceling a drawing used Undo and could discard the pre-existing redo branch. Slider movement also produced excessive history. | Begin/Commit/Cancel transactions preserve redo and group gestures; capture loss cancels incomplete edits. | Cancel restore, cancel preserves redo, one gesture/one undo, no-op history. |
| Cropping rendered/flattened the composition, losing editable annotations. | Crop the base raster and translate editable layer geometry/IDs. | Crop layer retention, coordinates, undo, empty/outside/subpixel bounds. |
| Broad outline hit boxes could select a hollow shape over the object inside it. | Geometric edge tests for outlines and segment tests for lines. | Hollow rectangle does not steal the interior selection. |
| Fractional redaction bounds could leave partially covered edge pixels. | Opaque redaction expands outward to integer image-pixel bounds before full-resolution export. | Fractional-edge pixel checks, unchanged base source. |
| Editor image load and source hashing were separate reads. | Both originate from one bounded byte snapshot. | Code review; atomic/conflict tests cover later writes. |
| Original-save guards were checked before lengthy encoding and new filenames could be overwritten in a race. | Checked writes compare the expected hash again, deny ordinary concurrent writers, require backups and use no-clobber creation for a new destination. | External-edit refusal, backup failure, preserved backup, name collision. This is not a defense against a malicious process racing renames. |
| A copied edited image must not carry the unedited source as a file-drop reference. | Copy result passes only flattened edited pixels to the shared clipboard helper. | Static call-path guard; actual paste targets remain a Windows acceptance item. |
| Cloning a zero-alpha clipboard DIB through GDI+ before inspecting it can lose its RGB. | Read original bitmap rows, repair all-zero alpha only in the DIB fallback, honor true partial alpha, bypass repair for PNG. | Raw BGRA repair, preserved partial alpha, transparent PNG, invalid buffers. |
| Mirrored EXIF orientations 5/7 used the wrong transform pairing. | Transpose/transverse ordering corrected and isolated. | Asymmetric six-pixel swatch corner tests. |
| A clipboard producer could replace the clipboard during delayed format rendering; repeated notifications could collect it twice. | Recheck the sequence after reading, track pending/handled sequences and invalidate work across pause/settings generations. | Code-path audit; actual producer races remain native tests. |
| Watcher continuations could outlive the watcher; filesystem bursts could produce excessive concurrent work. | Cancellation, disposed checks after waits, two workers, bounded pending queues, stable writes, asynchronous baseline and periodic reconciliation. | Static guardrails; live rename/disposal/burst tests remain manual. |
| Equivalent shortcut strings could evade duplicate checks. | Canonical modifier/key parser, reordered/synonym equivalence, valid virtual-key ranges, repeated-modifier rejection. | Canonical equality, equivalent conflicts, disabled slots, Unicode rejection, function bounds. |
| COM/GDI resources and failed animation initialization needed tighter ownership. | Release our Shell data-object reference; guard partial HDC/HBITMAP acquisitions; idempotent animation disposal; no-activate handling. | Code review only; handle-count endurance still unmeasured. |
| Session restoration could decode an arbitrary list before applying capacity. | Bound the restore list to the most recent 12 entries before decoding. | Source guard, native restore checklist. |

Save As also refuses a destination currently owned by a different open Pegline editor, preventing the editor registry from dropping the other window's unsaved-session tracking. Editor deactivation explicitly cancels incomplete gestures. These paths still need native multi-window acceptance.

## Product and interaction additions

**Local screenshot library.** A separate, searchable window holds up to 200 recent file references without changing the original shelf capacity. Search filename/folder; edit, copy, re-hang, open or reveal; import local images; forget a reference without deleting its file. Thumbnail work is asynchronous. No OCR, embeddings, cloud search or database dependency is introduced.

**Precise capture.** Repeat last region with a monitor-layout fingerprint; pixel loupe with a toggle; arrow-key cursor movement (Shift for 10 pixels); delay controls in the chooser; corrected square constraints. Capture still uses the existing visible-desktop backend and its documented limits.

**Editor usability.** Quick tools, keyboard accelerators, strong Done/Copy actions, saved/dirty status, base-image comparison, cursor-anchored zoom, actual-size/fit, annotation nudges, line angle snapping, crop guides, transaction-based sliders, highlighter defaults, full-resolution magnifier export and cached preview rendering.

**Shelf input architecture.** The interactive transparent window renders only cards. Rope/shadows and clips/status render in separate always-click-through decoration windows. This removes pointer polling as the decision that enables card input; the OS must still be tested for per-pixel pass-through, stacking, DPI, desktop switching and focus. Hover/press interpolation and fade-out feedback were revised. No true backdrop-blur implementation is claimed.

**Appearance/accessibility.** Shared rounded controls, light/dark resources, contrast-aware colors, theme-change subscription cleanup, named controls, virtual shelf automation peers and keyboard access through the library. Accessibility and rendered appearance are not certified without a Windows run.

**Pause and quiet arrival.** Pause automatic collection while keeping explicit actions available. Optional quiet arrivals keep new items from automatically revealing the line. Existing clipboard capture remains source-aware; general image collection stays opt-in.

## Dependency/privacy scope

Application runtime dependencies remain framework/platform assemblies: WPF, WinForms, Win32/Shell and .NET Framework 4.8. UI Automation adds platform assembly references, not a third-party package. No NuGet/npm package, browser engine, account, telemetry, updater, remote configuration, background service or installer was added. Both Windows build scripts remain offline.

The library is local metadata. Clipboard exclusions request no OS history/cloud upload but are not a universal privacy guarantee. Recovery backups still contain original pixels; redaction must not be confused with erasing those backups.

## Verification additions

There are 111 named C# regression cases (62 added), plus a separate actual-WPF visual smoke harness. **Zero C# cases and zero Windows renders were executed in this authoring environment.** The source/configuration audit and independent lexical scan are recorded separately. See `../VERIFICATION.md`.

The native acceptance checklist now explicitly covers the three overlay windows, clipboard sequences, collection pause/disposal, saved revisions, crop editability, DIB alpha, EXIF orientation, repeat-region invalidation, keyboard/UIA access and the new library. Source changes are not substitutes for those observed tests.

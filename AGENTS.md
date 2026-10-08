# Pegline development handoff

## User's accepted goal

Recreate the Tendedero screenshot clothesline on Windows feature-for-feature in user outcomes, look, animation and spatial behavior. Custom Windows capture/editor implementations are wanted. Keep dependencies minimal. No telemetry, analytics, crash upload, networking client, account system, automatic updater, remote settings or silent system screenshot-setting changes. Use a different app name/icon and retain source attribution.

## Current Windows status

0.2.1 beta has compiled on Windows; 113 regression tests and six real WPF renders passed, as did native shelf lifecycle checks. See VERIFICATION.md and docs/LOCAL_REVIEW.md. The following 0.2 source handoff is historical; its not-run statements no longer describe the current build. Full desktop acceptance is still outstanding.

## Historical source-delivery facts

This source delivery was authored on Linux without a C# compiler or a Windows desktop. Application C# compilation, the 111 authored tests and all Windows GUI tests are NOT RUN. `docs/static-audit.json` is static evidence only. Do not call the app working or fully parity-tested on the strength of that report. No prebuilt executable is included.

The chosen offline build targets C# 5, .NET Framework 4.8, x64, using WPF/WinForms/Win32 and framework assemblies only. Maintain compatibility unless a change is deliberately documented and justified. Do not add a browser shell, package restore, private virtual desktop API, or low-level key logger to paper over integration issues.

## 0.2 review handoff

Read `docs/REVIEW_CHANGES.md` before changing code. Preserve `Mark.NewCopy` versus snapshot `Clone`, revision-based save state, cancellable transactions, checked no-clobber saves, edited-copy-without-original-path, clipboard sequence/generation checks and bounded watcher lifecycle. The shelf is now three coordinated HWNDs: card input in the center, decorative layers that never accept input behind/in front. Do not reintroduce global click-through toggling based on pointer polling.

New files: `LibraryWindow.cs`, `ShelfAutomation.cs`, `tests/VisualSmoke.cs`, `Verify-Windows.cmd`. All remain uncompiled here. After fixing actual compiler failures, run `Verify-Windows.cmd` on an interactive Windows desktop and inspect its six synthetic-input WPF renders. Its reflection hook is test-only and must track any document-field rename. This harness must not read real screenshots/clipboard or start Controller.Start.

## First native execution task

On an interactive Windows 11 x64 machine:

1. Review `build.cmd`, then run it from the repository root using `cmd /c build.cmd`.
2. Fix compiler errors, then rerun until application and tests compile and all 111 tests pass. Add regression tests for fixes. Keep compiler/test logs; never replace failures with handwritten success output.
3. Launch `bin\Pegline.exe` in a regular, non-elevated desktop session. Use disposable images and an isolated test folder. Do not erase or rewrite real personal screenshots to test file handling.
4. Work through `docs/WINDOWS_ACCEPTANCE.md`, recording observed results, OS/framework/display details and source version. Focus/clipboard/drag/DPI cases require actual GUI execution.
5. Compare actual visual output to the original reference behavior; resolve material/animation and functional gaps before declaring parity.
6. Update `VERIFICATION.md` only with newly observed evidence. Preserve explicit caveats for untested cases. Package only a genuinely built artifact as an executable release.

## Key code locations

`Core.cs`: display state, original geometry, dedup, persistence helpers. `Controller.cs`: policy and integration. `Shelf.cs`/`Flight.cs`: overlay and animation. `Capture.cs`: self-contained physical-coordinate capture. `EditorModel.cs`/`EditorSurface.cs`/`EditorWindow.cs`: custom editor. `Native.cs`: Windows boundaries. `Storage.cs`/`Imaging.cs`: stable ingress, owned files and image/clipboard handling.

## File-safety invariants

Taking down external files does not delete them. Clear/overflow never deletes images. Owned discard goes through Recycle Bin with a verified inbox boundary. Do not implement permanent deletion as an error fallback. Shell moves must distinguish optimized/unoptimized completion and protect canceled/failed transfers. Editor writes preserve originals on failure and back up before atomic replacement. Recovery files contain original pixels and are not automatically purged.

Windows capture shortcuts coexist with Snipping Tool; exact flight coordinates come from our capture path. External images with unknown capture area must not be assigned guessed exact bounds. No installation, publishing, paid cloud resources or changes to connected repositories are authorized by this file.

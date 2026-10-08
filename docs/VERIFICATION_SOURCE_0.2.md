# Verification status — Pegline 0.2 reviewed source

## What is actually established

This is a revised source package, **not a compiled or Windows-tested release**. It contains no prebuilt executable. Source inspection found concrete defects and the code was changed to address them. Source/configuration audits ran locally; C# compilation, all C# test execution, and Windows desktop acceptance remain **NOT RUN**.

The active authoring environment is Linux. A check for `dotnet`, `mono`, `csc`, `mcs`, MSBuild and Wine found no usable toolchain; attempted external toolchain access did not succeed. No connected native Windows execution service was established. No repository was published, Windows CI job run, paid VM provisioned, or remote service installed.

| Evidence | Result |
| --- | --- |
| Application source reviewed/revised | 16 application C# files included. See `docs/REVIEW_CHANGES.md`. |
| Static source/configuration checks | 31 passed; 0 failed in the recorded run. See `docs/static-audit.json`. |
| Independent lexical/delimiter scan | 18 C# application/test files scanned. See `docs/independent-lexical-check.json`. This is not a parser/type checker. |
| Authored C# test cases | 111 individually named cases; 62 more than the supplied 0.1 package. |
| C# application compilation | **NOT RUN.** |
| C# test compilation/execution | **NOT RUN; 0 cases executed.** |
| Real WPF render harness execution | **NOT RUN.** `tests/VisualSmoke.cs` and `Verify-Windows.cmd` are provided for Windows execution. |
| Native capture, clipboard, Shell, focus, DPI, desktop or accessibility validation | **NOT RUN.** |
| Performance, idle CPU, memory/GDI endurance measurements | **NOT RUN.** |
| Signed binary, installer, parity certification | **Not produced.** |

## Why these checks are not a build

The Python source audit checks lexical structure, XML, compiler inputs, test inventory, dependency declarations and selected implementation guardrails. The separate Pygments scan checks tokenization and delimiter balance. Neither resolves C# types, overloads, target-framework API availability, COM ABI correctness, native event ordering, concurrency, hardware rendering, clipboard consumer behavior or operating-system file semantics. Compile-time and runtime fixes may still be required.

The 111 C# cases exercise image pixels, EXIF transforms, editor transactions/save points, cancellation/redo, crop and redaction behavior, file no-clobber/conflict rules, layout, visibility, shortcut normalization, bounded history, preferences and deduplication **when run on Windows**. Their presence is not evidence that they pass.

## How native evidence is produced

`Build-and-run.cmd` invokes `build.cmd`. Application compilation, test compilation and test execution must all succeed before automatic launch. Compiler output goes to `bin/build.log` and test output to `bin/test-results.txt`. An existing old executable is not evidence of a new successful build.

`Verify-Windows.cmd` runs the same build/test gate, then executes the real WPF rendering harness. It briefly opens test windows and generates six light/dark shelf, editor and library PNGs in `bin/visual-smoke`, plus `visual-smoke.txt` and `bin/visual-smoke.log`. Inputs are synthetic images in a GUID-named temporary store. The harness does not start the real controller's watchers/hotkeys, capture the desktop or read the clipboard. It does not render a HTML mockup. It also does not prove mouse pass-through, Shell transfers, visual parity or multi-monitor behavior. Inspect those images and complete the manual acceptance matrix separately.

Do not relabel any NOT RUN entry until a real execution provides evidence for the exact source version.

## Known limits retained

- Translucent frames approximate the reference material; there is no true live backdrop blur.
- Exact source bounds are available for Pegline's own capture path. External images use a drop-in, not a fabricated location.
- Repeat-region capture is guarded by monitor names and physical bounds. Scale-only changes with unchanged bounds retain the physical-pixel region; the fingerprint does not include DPI. It does not follow a moved window.
- WebP/HEIF depend on installed Windows codecs. Animated/multipage inputs are represented by their first frame; unsupported overwrite uses Save As.
- GDI visible-desktop capture does not promise HDR, protected content, secure desktop or offscreen-window content.
- Crop retains editable layers. Rotate/flip still flatten the current composition, with undo available. Saved outputs are flattened.
- Base-image preview is the current base raster, including committed crop/rotate/flip, not a separate untouched-original recovery viewer.
- The shelf exposes virtual UI Automation peers and the library supplies a keyboard route; neither constitutes a completed accessibility audit.
- Backups in Recovery retain original/pre-redaction pixels. They are local, not encrypted by Pegline, and are not automatically purged.
- Requested Clipboard History/Cloud Clipboard exclusions depend on OS behavior and do not govern third-party clipboard tools.

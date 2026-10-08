# Windows review — 2026-10-08

Scope: retrieved Pegline 0.2 source, reviewed build scripts, editor history, file writes, capture intake, Shell transfer cleanup, window composition and visual output. This is a focused review, not a complete security audit or feature-parity certification.

## Fixed findings

1. **Build blocker:** two clipboard retry loops used `await` inside `catch`, unsupported by the declared C# 5 compiler. Moved delays after the catch while preserving final-attempt exceptions and immediate success returns.
2. **Build blocker:** integer zero made `LinearGradientBrush` overload selection ambiguous. Specified floating-point angle `0f`.
3. **Undo regression:** beginning a gesture trimmed the oldest committed snapshot before knowing whether the gesture would commit. Cancel/no-op could silently lose history. Deferred trimming until commit; added two regression tests at the 32-entry limit.
4. **Editor layout:** fixed sidebar width plus margins exceeded available scroll-view width. Removed fixed child width.
5. **Contrast:** native dropdown chrome remained light while inheriting white text. Explicit compatible dropdown colors now keep labels readable. Library selected rows now use a controlled template so foreground/background remain paired, including inactive selection.
6. **Verification harness:** content rendering omitted window background and clipped content offsets. Composed opaque backgrounds and rendered the content through a VisualBrush.
7. **Windows tooling:** static audit relied on platform-default text encoding; Spanish source text failed to decode on Windows. All source reads now explicitly use UTF-8.

## Executed evidence

- Built application and tests using Windows .NET Framework C# 5 compiler, x64.
- 113 C# tests passed, zero failed. Includes overwrite-conflict refusal, backups, no-clobber saves, redaction coverage, EXIF orientation, editor history and crop behavior.
- Six actual WPF renders generated with synthetic inputs; light/dark editor, library and shelf inspected.
- Native three-window shelf creation, show, no-activate/input styles, hide and close passed in the smoke harness.
- 31 static checks passed. Static checker deliberately reports zero C# executions for its own scope; runtime evidence is separate.

Initial six file tests failed under restricted system temporary-directory permissions. The same tests passed with TEMP/TMP scoped to `bin/test-temp`; no file-safety behavior was weakened. The normal GitHub runner uses its standard temporary directory.

## Remaining acceptance work

Real clipboard destinations, interactive capture selection, Shell drag/move/cancel receivers, focus pass-through, mixed-DPI/multi-monitor changes, virtual desktops, long-running resource behavior and full upstream parity remain unverified. Native style assertions do not prove real click-through behavior.

Framework compilation emits two existing `FormattedText` obsolete-overload warnings. Recovery backups deliberately retain original, potentially unredacted pixels. Application is unsigned. No updater, telemetry, remote service, or third-party runtime package was introduced.

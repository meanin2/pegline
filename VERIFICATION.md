# Windows verification — 0.2.1 beta

Verified locally on October 8, 2026, Windows build 26200.9457 (25H2), x64, .NET Framework CLR 4.0.30319.42000.

| Check | Observed result |
| --- | --- |
| Original downloaded archive checksums | All 41 listed entries passed |
| Application and test compilation | Passed with local C# 5 compiler |
| C# regressions | 113 passed, 0 failed |
| Actual WPF shelf/editor/library rendering | Six light/dark PNGs generated and inspected |
| Native shelf window lifecycle | Create/show/styles/hide/close passed |
| Static source audit | 31 passed, 0 failed |
| Full interactive desktop acceptance | Not completed |

Run `Verify-Windows.cmd` to reproduce compilation, regressions and synthetic rendering. `bin/build.log`, `bin/test-results.txt`, `bin/visual-smoke.log` and `bin/visual-smoke/` contain generated evidence. Public CI publishes these artifacts after each run. Runtime and static results are separate: static audit's zero-executed-tests field describes only that Python tool.

Tests used an isolated temporary directory. Local sandbox required TEMP/TMP inside `bin/test-temp` for atomic file replacement permissions. No personal screenshot files were used. WPF smoke creates real controls and shelf HWNDs; it does not establish end-to-end clipboard, capture, drag/drop, focus, DPI or virtual-desktop behavior.

Two existing FormattedText overload deprecation warnings remain. The release is unsigned. File overwrite tests confirm checked replacement and backup behavior under tested conditions, not protection against malicious concurrent processes. Recovery backups retain original pixels. Full parity is not claimed.

[Review and fixes](docs/LOCAL_REVIEW.md) · [Desktop acceptance checklist](docs/WINDOWS_ACCEPTANCE.md) · [Historical source-only record](docs/VERIFICATION_SOURCE_0.2.md)


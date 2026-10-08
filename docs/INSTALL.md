# Download and install Pegline

Pegline is a portable Windows application. The website's shelf and markup demos are browser simulations; download the application for real desktop integration.

## Requirements

Windows 11 x64 with .NET Framework 4.8 or 4.8.1. No account, subscription, administrator rights, package manager, or installer is required. ARM-native and Windows 10 acceptance have not been tested.

## Download and first launch

1. Open [GitHub Releases](https://github.com/meanin2/pegline/releases) and select the newest Windows beta. Under **Assets**, download `Pegline-<version>-win-x64.zip`. The GitHub-generated **Source code** archives are for developers and do not contain the built application.
2. Optionally download the matching `.zip.sha256` file. In PowerShell, run `Get-FileHash .\Pegline-<version>-win-x64.zip -Algorithm SHA256` using the actual downloaded filename. Its hash must match the checksum file. A checksum detects a mismatched download; it does not substitute for a publisher signature.
3. In File Explorer, right-click the ZIP and select **Extract All**. Choose a stable folder such as `Documents\Pegline`. Do not run the executable from inside the ZIP viewer.
4. Double-click `Pegline.exe` in the extracted folder. Keep `Pegline.exe.config` alongside it. This beta is unsigned; Windows may show an unknown-publisher warning. Review the source and release information before deciding whether to run it. If your organization blocks it, follow your administrator's policy.
5. Pegline runs in the system tray near the clock; expand hidden icons if needed. Right-click its icon for captures, settings, library, and **Quit**. It does not open a permanent main window.
6. Press `Ctrl+Alt+4` for a region capture, or `Ctrl+Alt+T` to show the shelf. An empty shelf has no image cards. Start with a disposable screenshot. Click a card to copy, hold for Markup, or use its context menu.

Pegline coexists with `Win+Shift+S`; it does not replace Snipping Tool. Shortcut conflicts are reported; choose another shortcut in Settings. Automatic folder and clipboard collection can be paused from the tray. General clipboard-image collection is separately opt-in.

## Update

1. If moving to a new application folder, turn off **Open at login** before moving the old version.
2. Choose **Quit** from Pegline's tray menu.
3. Extract the new release. Keep its executable and configuration together; do not overwrite a running executable.
4. Start the new version. Preferences, library references, inbox captures, and recovery backups remain under `%LOCALAPPDATA%\Pegline`. Re-enable **Open at login** if desired. There is no automatic updater.

## Remove

Turn off **Open at login**, quit Pegline, then remove its extracted application folder. This leaves `%LOCALAPPDATA%\Pegline` intact. Review and save any wanted Inbox captures, Recovery originals, and signatures before manually removing that data folder. External screenshots remain at their original locations.

## Troubleshooting

- **Nothing appears:** check hidden tray icons; press `Ctrl+Alt+T`. Only one normal application instance runs at a time.
- **Shortcut unavailable:** change the conflicting binding in Settings.
- **Image will not open:** PNG/JPEG are the baseline formats. HEIF/WebP need compatible installed Windows codecs.
- **Missing framework/compiler:** running the release requires .NET Framework; building source also needs local compiler/WPF assemblies. No script downloads these automatically.
- **Need to build source:** follow [BUILDING.md](BUILDING.md). `Build-and-run.cmd` compiles/tests before launching; `Verify-Windows.cmd` also generates synthetic WPF renders.

Read [FEATURE_PARITY.md](FEATURE_PARITY.md) for verified behavior, Windows adaptations, and open acceptance checks. Full desktop interaction acceptance remains pending.

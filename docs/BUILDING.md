# Local Windows build

The primary path is `Build-and-run.cmd`, not an online package-manager bootstrap. Extract the whole source archive first.

## Platform

Initial target is Windows 11 x64, .NET Framework 4.8/4.8.1, and WPF. The code deliberately uses C# 5 syntax so the classic Windows .NET Framework compiler can compile it without a separate modern SDK. There are no third-party package references.

The build searches:

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
```

It expects the framework and WPF assemblies under that selected framework directory. The fallback compiler can produce an x64 binary; the application itself is not an x86 build. Availability varies with Windows image configuration. Missing components are reported, not silently installed.

## Scripts

`build.cmd` compiles the application and tests and then runs the tests. It returns a failing exit code if any stage fails. `Build-and-run.cmd` calls it and starts the application only after success. No PowerShell execution-policy changes, elevation, NuGet restore, remote script, or download is used.

The script's use of local runtime assemblies is practical for this source build. A conventional IDE/release build can instead use the Framework 4.8 reference assemblies through the included classic `Pegline.csproj`.

Required framework references are declared in the script/project: System, System.Core, System.Drawing, System.Windows.Forms, System.Xml, System.Xaml, System.Runtime.Serialization, Microsoft.VisualBasic, WindowsBase, PresentationCore, PresentationFramework, UIAutomationProvider and UIAutomationTypes. `Microsoft.VisualBasic` is a Windows framework assembly used only for Recycle Bin operations, not an extra language/runtime package.

## Evidence and failure handling

A successful compile writes `bin\Pegline.exe` and its `.exe.config`. Test results go to `bin\test-results.txt`; compiler output goes to `bin\build.log`.

If compilation or tests fail, keep those files. The test program returns 1 when a case fails. The launcher does not label this a successful build and does not start the app. An older binary may still exist from an earlier run; its existence is not evidence that the new source compiled. Read the current logs.

If an already running Pegline locks the executable, quit using the tray menu before rebuilding. Source delivery has not been compiled here; corrections may be needed after the first native build.

## Optional real-WPF visual smoke run

`Verify-Windows.cmd` first runs the same application/test build gate, then calls:

```text
bin\Pegline.Tests.exe --render-smoke "%CD%\bin\visual-smoke"
```

It needs an interactive Windows desktop. It briefly opens synthetic-image editor/library windows and renders their actual WPF visual trees, plus the production shelf drawing code, in light/dark modes. The app controller is constructed with an isolated temporary store but is not started, so no live watcher, clipboard intake, capture or shortcut registration runs. Six PNGs and a text result go to `bin\visual-smoke`; the console log goes to `bin\visual-smoke.log`. Cleanup applies only to the GUID-named temporary folder created by the harness.

No smoke images or log are included as successful output in this source delivery. A successful render is also not a pixel-perfect parity certification or a native interaction test. Inspect the result and run the acceptance checklist.

## Optional IDE build

Open `Pegline.csproj` in an appropriate Windows Visual Studio/MSBuild installation with .NET desktop and Framework 4.8 targeting support. The IDE path is optional and is not used by the offline `.cmd` script. Building the project alone does not execute the test program; run `build.cmd` for the documented test gate.

## Optional maintainer audit

```text
python tools\static_audit.py --json docs\static-audit.json
```

This uses the Python standard library but Python is **not** an app/build runtime dependency. The separate optional `tools/lexical_audit.py` additionally requires Pygments on the maintainer machine; it is not called by Windows build/verification scripts. This audit is deliberately limited; it cannot establish C# type correctness or Windows runtime behavior. A fresh run replaces the static report, not the separate native build/test status.

## Deployment and removal

After a successful build and native acceptance, the `.exe` and `.exe.config` can be copied together to a stable local folder on a suitable Windows PC. Enabling “Start with Windows” creates a user-level Run entry for that exact executable path. Turn startup off before moving or removing the executable.

The source has no automatic updater or telemetry. Updating is an explicit manual replacement after quitting the app. User captures, settings, and backups remain under `%LOCALAPPDATA%\Pegline`; removing binaries does not silently remove those files. Inspect and keep/export files before deliberately removing that data folder.

The executable produced by this script is unsigned. This package does not instruct users to disable Windows protections or guarantee that a managed PC will allow it. Signing and organizational allow-listing, when needed, are separate release tasks.

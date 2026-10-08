@echo off
setlocal EnableExtensions
pushd "%~dp0"
call build.cmd
if errorlevel 1 (
  popd
  exit /b 1
)
echo Rendering the actual WPF controls with synthetic test images.
echo This will briefly open isolated test windows; it does not read your clipboard or capture your screen.
if not exist bin\visual-smoke mkdir bin\visual-smoke
bin\Pegline.Tests.exe --render-smoke "%CD%\bin\visual-smoke" > bin\visual-smoke.log 2>&1
if errorlevel 1 (
  type bin\visual-smoke.log
  echo VISUAL SMOKE FAILED. Keep the log with the source version for diagnosis.
  popd
  exit /b 1
)
type bin\visual-smoke.log
bin\Pegline.Tests.exe --integration-smoke > bin\integration-smoke.log 2>&1
if errorlevel 1 (
  type bin\integration-smoke.log
  popd
  exit /b 1
)
type bin\integration-smoke.log
echo Inspect bin\visual-smoke and complete docs\WINDOWS_ACCEPTANCE.md.
popd
exit /b 0

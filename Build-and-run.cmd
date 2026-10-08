@echo off
setlocal EnableExtensions
pushd "%~dp0"
call build.cmd
if errorlevel 1 (
  echo.
  echo The source package has not built successfully. Keep bin\build.log and bin\test-results.txt for diagnosis.
  pause
  popd
  exit /b 1
)
echo Starting Pegline. Its icon lives in the Windows notification area.
start "" "%CD%\bin\Pegline.exe"
popd
exit /b 0

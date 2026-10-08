@echo off
setlocal EnableExtensions
pushd "%~dp0"
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FX%\csc.exe" set "FX=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FX%\csc.exe" (
  echo The Windows .NET Framework compiler was not found.
  echo This package targets Windows 11 x64 with .NET Framework 4.8 or 4.8.1.
  echo Nothing was downloaded or installed.
  popd
  exit /b 1
)
if not exist "%FX%\WPF\PresentationFramework.dll" (
  echo The Windows WPF assemblies are unavailable in %FX%.
  popd
  exit /b 1
)
if not exist bin mkdir bin
set "REFS=/r:"%FX%\System.dll" /r:"%FX%\System.Core.dll" /r:"%FX%\System.Drawing.dll" /r:"%FX%\System.Windows.Forms.dll" /r:"%FX%\System.Xml.dll" /r:"%FX%\System.Xaml.dll" /r:"%FX%\System.Runtime.Serialization.dll" /r:"%FX%\Microsoft.VisualBasic.dll" /r:"%FX%\WPF\WindowsBase.dll" /r:"%FX%\WPF\PresentationCore.dll" /r:"%FX%\WPF\PresentationFramework.dll" /r:"%FX%\WPF\UIAutomationProvider.dll" /r:"%FX%\WPF\UIAutomationTypes.dll""
echo Building Pegline from local source. No downloads or package restore.
"%FX%\csc.exe" /nologo /noconfig /langversion:5 /codepage:65001 /utf8output /platform:x64 /optimize+ /debug:pdbonly /warn:4 /target:winexe /main:Pegline.Program /win32manifest:app.manifest /win32icon:assets\pegline.ico /out:bin\Pegline.exe %REFS% /recurse:src\*.cs > bin\build.log 2>&1
if errorlevel 1 (
  type bin\build.log
  echo BUILD FAILED. See bin\build.log. No successful build is claimed.
  popd
  exit /b 1
)
copy /y app.config bin\Pegline.exe.config >nul
"%FX%\csc.exe" /nologo /noconfig /langversion:5 /codepage:65001 /utf8output /platform:x64 /optimize+ /warn:4 /target:exe /main:Pegline.Tests.TestProgram /win32manifest:app.manifest /out:bin\Pegline.Tests.exe %REFS% /recurse:src\*.cs tests\TestProgram.cs tests\VisualSmoke.cs tests\IntegrationSmoke.cs >> bin\build.log 2>&1
if errorlevel 1 (
  type bin\build.log
  echo TEST BUILD FAILED. See bin\build.log.
  popd
  exit /b 1
)
copy /y app.config bin\Pegline.Tests.exe.config >nul
bin\Pegline.Tests.exe > bin\test-results.txt 2>&1
if errorlevel 1 (
  type bin\test-results.txt
  echo TESTS FAILED. The application will not be launched automatically.
  popd
  exit /b 1
)
type bin\test-results.txt
echo.
echo Built bin\Pegline.exe. Native desktop acceptance checks remain separate.
popd
exit /b 0

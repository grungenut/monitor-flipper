@echo off
rem Builds MonitorFlipper.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem No installs needed. Double-click this file, or run it from a terminal.
rem The WinMetadata files (Windows' own API descriptions) are used for single-window capture.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WM=%WINDIR%\System32\WinMetadata
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"%~dp0assets\MonitorFlipper.ico" /out:"%~dp0bin\MonitorFlipper.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Graphics.winmd" ^
  "%~dp0src\*.cs"
if errorlevel 1 (echo Build FAILED. & exit /b 1)
echo Built: %~dp0bin\MonitorFlipper.exe

@echo off
setlocal enabledelayedexpansion

rem ------------------------------------------------------------------
rem  TimeLock build
rem  Uses the C# compiler that ships with Windows (.NET Framework 4.x),
rem  so there is nothing to install first.
rem ------------------------------------------------------------------

set "ROOT=%~dp0"
set "SRC=%ROOT%src"
set "OUT=%ROOT%bin"

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [x] No C# compiler found. Expected csc.exe under C:\Windows\Microsoft.NET.
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

set "REFS=/r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll"

set "COMMON=/nologo /optimize+ /platform:anycpu /langversion:5 /target:winexe %REFS%"

rem Icon is optional so a clone without the generated asset still builds.
rem Regenerate it with:  py -3 tools\make_logo.py
set ICON=
if exist "%ROOT%assets\timelock.ico" set ICON=/win32icon:"%ROOT%assets\timelock.ico"

echo Building TimeLock.Client.exe ...
"%CSC%" %COMMON% %ICON% /out:"%OUT%\TimeLock.Client.exe" "%SRC%\Common.cs" "%SRC%\Client.cs"
if errorlevel 1 goto :fail

echo Building TimeLock.Enforce.exe ...
"%CSC%" %COMMON% %ICON% /out:"%OUT%\TimeLock.Enforce.exe" "%SRC%\Common.cs" "%SRC%\Enforcer.cs"
if errorlevel 1 goto :fail

echo Building TimeLock.App.exe ...
"%CSC%" %COMMON% %ICON% /win32manifest:"%SRC%\app.manifest" /out:"%OUT%\TimeLock.App.exe" "%SRC%\Common.cs" "%SRC%\App.cs"
if errorlevel 1 goto :fail

echo.
echo [ok] Build complete in %OUT%
echo.
echo Next: right-click install\Install.ps1 and choose "Run with PowerShell"
echo (or run:  powershell -ExecutionPolicy Bypass -File "%ROOT%install\Install.ps1")
exit /b 0

:fail
echo.
echo [x] Build failed.
exit /b 1

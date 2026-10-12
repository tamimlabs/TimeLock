@echo off
setlocal
title TimeLock Setup

rem ---------------------------------------------------------------------
rem  Double-click this file.
rem  It asks Windows for administrator rights (required to register the
rem  SYSTEM scheduled task) and then runs Install.ps1.
rem ---------------------------------------------------------------------

echo.
net session >nul 2>&1
if not errorlevel 1 goto :run

rem Not elevated -- relaunch this same file elevated.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
if errorlevel 1 goto :denied
exit /b 0

:denied
echo   [!!] Administrator rights are required to install TimeLock.
echo        Please right-click this file and choose "Run as administrator".
echo.
pause
exit /b 1

:run
echo.
echo   TimeLock setup
echo   --------------
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo   Setup reported an error (exit code %RC%).
echo   Press any key to close this window.
pause >nul
endlocal & exit /b %RC%
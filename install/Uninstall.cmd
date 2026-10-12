@echo off
setlocal
title TimeLock Removal

rem ---------------------------------------------------------------------
rem  Double-click this file to remove TimeLock.
rem  Administrator rights are required to delete the scheduled task and
rem  the program folder.
rem ---------------------------------------------------------------------

echo.
net session >nul 2>&1
if not errorlevel 1 goto :run

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
if errorlevel 1 goto :denied
exit /b 0

:denied
echo   [!!] Administrator rights are required to remove TimeLock.
echo        Please right-click this file and choose "Run as administrator".
echo.
pause
exit /b 1

:run
echo.
echo   Removing TimeLock
echo   -----------------
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
set "RC=%ERRORLEVEL%"
echo.
echo   Press any key to close this window.
pause >nul
endlocal & exit /b %RC%
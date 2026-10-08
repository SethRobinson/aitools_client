@echo off
setlocal DisableDelayedExpansion
if "%~dp0"=="" exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ReleasePackage.ps1" -Phase Stage
if errorlevel 1 exit /b 1
if "%NO_PAUSE%"=="" pause
exit /b 0

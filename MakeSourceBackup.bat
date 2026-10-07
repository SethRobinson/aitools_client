@echo off
setlocal DisableDelayedExpansion
if "%~dp0"=="" exit /b 1
pushd "%~dp0" || exit /b 1
call "%~dp0app_info_setup.bat"
if errorlevel 1 goto failed
if not defined PROTON_DIR goto failed

set "THE_DATE="
call "%PROTON_DIR%\shared\win\utils\RTGetDate.bat" > "%~dp0tmpDate.txt"
set /p THE_DATE= < "%~dp0tmpDate.txt"
del /Q "%~dp0tmpDate.txt"
powershell -NoProfile -Command "if ([string]::IsNullOrWhiteSpace($env:THE_DATE) -or $env:THE_DATE -cnotmatch '\A[A-Za-z0-9_-]+\z') { exit 1 }"
if errorlevel 1 goto failed


set "FNAME=aitools_client_Source_%THE_DATE%.zip"
cd /d "%~dp0.." || goto failed
if exist "%~dp0..\aitools_client_Source_%THE_DATE%.zip" del /Q "%~dp0..\aitools_client_Source_%THE_DATE%.zip"
:-x!*.svn -
%PROTON_DIR%\shared\win\utils\7za.exe a -r -tzip %FNAME% %APP_NAME%\* base_setup.bat -x!*.zip -x!*.csproj -x!*.sln -x!log.txt -x!*.ncb -x!*.bsc -x!*.pdb -x!*.sbr -x!*.ilk -x!*.idb -x!.o -x!*.obj -x!*.DS_Store -x!._* -x!%APP_NAME%\dist -x!%APP_NAME%\build\web -x!%APP_NAME%\build\win -x!%APP_NAME%\.vs -x!%APP_NAME%\dist -x!%APP_NAME%\Library -x!%APP_NAME%\Logs -x!%APP_NAME%\Temp -x!%APP_NAME%\webgltemp
if errorlevel 1 goto failed
if "%NO_PAUSE%"=="" pause
popd
exit /b 0

:failed
echo Source backup stopped because setup or filename validation failed.
popd
exit /b 1

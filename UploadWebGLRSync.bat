@echo off
setlocal DisableDelayedExpansion
if "%~dp0"=="" exit /b 1
pushd "%~dp0" || exit /b 1
call "%~dp0app_info_setup.bat"
if errorlevel 1 goto failed
cd /d "%~dp0" || goto failed
if not defined BUILDMODE set "BUILDMODE=BETA"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ValidateWebGLUpload.ps1"
if errorlevel 1 goto failed
if not exist "%~dp0webgltemp\%APP_NAME%\Build\" goto failed

REM All remote deletes live in the guarded script; beta cleanup matches the upload destination.
wsl ssh "%_FTP_USER_%@%_FTP_SITE_%" sh -s -- "%WEB_SUB_DIR%" "%BUILDMODE%" < "%~dp0scripts\CleanWebGL.sh"
if errorlevel 1 goto failed
if "%BUILDMODE%"=="RELEASE" (
    wsl rsync -avzr --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r -e "ssh" "webgltemp/%APP_NAME%/" "%_FTP_USER_%@%_FTP_SITE_%:www/%WEB_SUB_DIR%/"
) else (
    wsl rsync -avzr --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r -e "ssh" "webgltemp/%APP_NAME%/" "%_FTP_USER_%@%_FTP_SITE_%:www/%WEB_SUB_DIR%/beta/"
)
if errorlevel 1 goto failed
if "%BUILDMODE%"=="RELEASE" (
    echo Files synced uploaded: https://www.%_FTP_SITE_%/%WEB_SUB_DIR%
    start https://www.%_FTP_SITE_%/%WEB_SUB_DIR%
) else (
    echo Files synced uploaded: https://www.%_FTP_SITE_%/%WEB_SUB_DIR%/beta
    start https://www.%_FTP_SITE_%/%WEB_SUB_DIR%/beta
)
if "%NO_PAUSE%"=="" pause
popd
exit /b 0

:failed
echo WebGL upload stopped because setup, validation, cleanup, or upload failed.
if "%NO_PAUSE%"=="" pause
popd
exit /b 1

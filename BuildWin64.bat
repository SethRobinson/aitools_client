@echo off
setlocal DisableDelayedExpansion
if "%~dp0"=="" exit /b 1
pushd "%~dp0" || exit /b 1
call "%~dp0app_info_setup.bat"
if errorlevel 1 goto failed
cd /d "%~dp0" || goto failed
if not defined APP_NAME goto failed
if not defined UNITY_EXE goto failed
if not defined RT_UTIL goto failed
if not defined RT_PROJECTS goto failed

SET FILENAME=%APP_NAME%

:Actually do the unity build
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\CleanBuildOutput.ps1" -Phase Reset
if errorlevel 1 goto failed
echo Building project...

%UNITY_EXE% -quit -batchmode -logFile log.txt -executeMethod Win64Builder.BuildRelease -projectPath "%cd%" -activeBuildProfile "Assets/Settings/Build Profiles/ReleaseBuildProfile.asset"
if errorlevel 1 goto failed
echo Finished building.
if not exist "build\win\%APP_NAME%.exe" (
echo Error with build!
echo See "%~dp0log.txt" for Unity diagnostics.
goto failed
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ReleasePackage.ps1" -Phase Stage
if errorlevel 1 goto failed
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\CleanBuildOutput.ps1" -Phase Package
if errorlevel 1 goto failed


call "%RT_PROJECTS%\Signing\sign.bat" "build/win/%APP_NAME%.exe" "Seth's AI Tools" "rtsoft.com"
if errorlevel 1 goto failed
call "%RT_PROJECTS%\Signing\sign.bat" "build/win/utils/RTClip.exe" "RTClip" "rtsoft.com"
if errorlevel 1 goto failed
call "%RT_PROJECTS%\Signing\sign.bat" "build/win/utils/RTClip.dll" "RTClip" "rtsoft.com"
if errorlevel 1 goto failed

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ReleasePackage.ps1" -Phase Verify
if errorlevel 1 goto failed


:create the archive
set ZIP_FNAME=SethsAIToolsWindows.zip
if exist "%~dp0SethsAIToolsWindows.zip" del /Q "%~dp0SethsAIToolsWindows.zip"
cd /d "%~dp0build" || goto failed

"%RT_UTIL%\7za.exe" a -tzip "%~dp0SethsAIToolsWindows.zip" .\win\
if errorlevel 1 goto failed
cd /d "%~dp0" || goto failed
:Rename the root folder
"%RT_UTIL%\7z.exe" rn "%~dp0SethsAIToolsWindows.zip" win\ aitools_client\
if errorlevel 1 goto failed

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\ReleasePackage.ps1" -Phase VerifyArchive
if errorlevel 1 goto failed

if "%NO_PAUSE%"=="" pause
popd
exit /b 0

:failed
echo Build failed. Packaging stopped.
if "%NO_PAUSE%"=="" pause
popd
exit /b 1

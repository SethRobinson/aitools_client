if "%~dp0"=="" exit /b 1
if not defined RT_PROJECTS exit /b 1
pushd "%~dp0" || exit /b 1
if exist "%~dp0SethsAIToolsWindowsBeta.zip" del /Q "%~dp0SethsAIToolsWindowsBeta.zip"
rename "%~dp0SethsAIToolsWindows.zip" SethsAIToolsWindowsBeta.zip
call %RT_PROJECTS%\UploadFileToRTsoftSSH.bat SethsAIToolsWindowsBeta.zip files

pause
popd

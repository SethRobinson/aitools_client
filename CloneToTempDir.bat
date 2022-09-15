call app_info_setup.bat

rmdir ..\%APP_NAME%Temp%1 /S /Q
echo Cloning %APP_NAME% to temp dir %APP_NAME%Temp%1...
mkdir ..\%APP_NAME%Temp%1

if "%REBUILD_LIBRARY%"=="TRUE" (
xcopy /c . ..\%APP_NAME%Temp%1\ /E /F /Y  /EXCLUDE:%cd%\CloneExclusionListFullRebuild.txt
) else (
xcopy /c . ..\%APP_NAME%Temp%1\ /E /F /Y  /EXCLUDE:%cd%\CloneExclusionList.txt
)

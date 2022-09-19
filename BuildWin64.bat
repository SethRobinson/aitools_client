call app_info_setup.bat

SET FILENAME=%APP_NAME%

:Actually do the unity build
rmdir build\win /S /Q
mkdir build\win
call GenerateBuildDate.bat
echo Building project...

:So let's delete these things from the shared stuff that we don't need in this kind of build? (remove the : in front to delete)
:del /Q Assets\RT\MySQL\RTSqlManager.cs
:del /Q Assets\RT\RTNetworkServer.cs

:%UNITY_EXE% -quit -batchmode -logFile log.txt -buildWindows64Player build/win/%APP_NAME%.exe -projectPath %cd%
%UNITY_EXE% -quit -batchmode -logFile log.txt -executeMethod Win64Builder.BuildRelease -projectPath %cd%
echo Finished building.
if not exist build/win/%APP_NAME%.exe (
echo Error with build!
start notepad.exe log.txt
%RT_UTIL%\beeper.exe /p
pause
)

:Add a few more files we need
mkdir build\win\utils
xcopy /c utils build\win\utils\ /E /F /Y
del build\win\utils\RTClip.zip

call %RT_PROJECTS%\Signing\sign.bat "build/win/%APP_NAME%.exe" "Seth's AI Tools"
call %RT_PROJECTS%\Signing\sign.bat "build/win/utils/RTClip.exe" "RTClip"


:create the archive
set ZIP_FNAME=SethsAIToolsWindows.zip
del %ZIP_FNAME%
cd build
%RT_UTIL%\7za.exe a -r -tzip ..\%ZIP_FNAME% win
cd ..
:Rename the root folder
%RT_UTIL%\7z.exe rn %ZIP_FNAME% win\ aitools_client\

if "%NO_PAUSE%"=="" pause

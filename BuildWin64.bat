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
%UNITY_EXE% -quit -batchmode -logFile log.txt -executeMethod Win64Builder.BuildRelease -projectPath %cd% -activeBuildProfile "Assets/Settings/Build Profiles/ReleaseBuildProfile.asset"
echo Finished building.
if not exist build/win/%APP_NAME%.exe (
echo Error with build!
start notepad.exe log.txt
%RT_UTIL%\beeper.exe /p
pause
)

call UpdateBuildDirConfigFiles.bat
del build\win\Adventure\test*.txt
del build\win\AIGuide\TEST*.txt
del build\win\config.txt
del build\win\config_llm.txt
del build\win\ComfyUI\test*.json
del build\win\ComfyUI\TEST*.json
del build\win\ComfyUI\workflow\test*.json
del build\win\Presets\TEST*.txt

rd /s /q build\win\ComfyUI\Unused


call %RT_PROJECTS%\Signing\sign.bat "build/win/%APP_NAME%.exe" "Seth's AI Tools"
call %RT_PROJECTS%\Signing\sign.bat "build/win/utils/RTClip.exe" "RTClip"

del build\win\utils\RTClip.zip
rmdir /S /Q "build\win\Seth's AI Tools_BurstDebugInformation_DoNotShip"
rmdir /S /Q "build\win\autosave"
rmdir /S /Q "build\win\tempCache"


:create the archive
set ZIP_FNAME=SethsAIToolsWindows.zip
del %ZIP_FNAME%
cd build

%RT_UTIL%\7za.exe a -r -tzip ..\%ZIP_FNAME% win
cd ..
:Rename the root folder
%RT_UTIL%\7z.exe rn %ZIP_FNAME% win\ aitools_client\

if "%NO_PAUSE%"=="" pause

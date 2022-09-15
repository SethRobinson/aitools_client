call app_info_setup.bat

SET FILENAME=%APP_NAME%

:Actually do the unity build
rmdir build\web /S /Q
mkdir build\web

:Command line builds do NOT handle preprocesser commands like you'd expect at this time.  While they are properly ignored in the
:final build, the ignored part can still throw errors if a .dll etc is missing.  NOT GOOD

:So let's delete these things from the shared stuff that we'd never use in a client webgl build
del /Q Assets\RT\MySQL\RTSqlManager.cs
del /Q Assets\RT\RTNetworkServer.cs

del log.txt
call GenerateBuildDate.bat
echo Building project...
if "%BUILDMODE%"=="RELEASE" (
%UNITY_EXE% -quit -batchmode -logFile log.txt -executeMethod WebGLBuilder.Build -projectPath %cd%
) else (
%UNITY_EXE% -quit -batchmode -logFile log.txt -executeMethod WebGLBuilder.BuildBeta -projectPath %cd%

)
echo Finished building.

rmdir webgltemp /S /Q
mkdir webgltemp
mkdir webgltemp\%FILENAME%

xcopy build\web webgltemp\%FILENAME%\ /E /F /Y
rename webgltemp\%FILENAME%\web.html index.html
xcopy Packaging\web_override webgltemp\%FILENAME%\ /E /F /Y

if not exist webgltemp\%FILENAME%\index.html (
start notepad.exe log.txt
%PROTON_DIR%\shared\win\utils\beeper.exe /p
)

:apply string additions/inserts to index.html
cd webgltemp\%FILENAME%
call insert.bat
del index_insert.txt
del insert.bat
cd ..\..


if "%NO_PAUSE%"=="" pause

:Setting no pause causes our .bat files to not do pause commands when they are done
set NO_PAUSE=1
:First, let's customize the directory name we're going to close to, by adding a postfix to make it unique
SET CLONE_DIR_POSTFIX=WebGL
:Now let's actually make it, we'll pass in the postfix as a parm
call CloneToTempDir.bat %CLONE_DIR_POSTFIX%

:Move to build dir
cd ../%APP_NAME%Temp%CLONE_DIR_POSTFIX%
:Do the actual build
call BuildWebGL.bat

:destroy any old webgl builds in our main dir
rmdir ..\%APP_NAME%\build\web /S /Q
rmdir ..\%APP_NAME%\webgltemp /S /Q

:recreate the dirs if needed.  We want to copy our finished products back to the main dir
mkdir ..\%APP_NAME%\build
mkdir ..\%APP_NAME%\build\web
mkdir ..\%APP_NAME%\webgltemp

:copy the final build back to our main dir
xcopy build\web ..\%APP_NAME%\build\web /E /F /Y
xcopy webgltemp ..\%APP_NAME%\webgltemp /E /F /Y


call UploadWebGLRsync.bat
:Move back out of it
cd ..

:for some reason it's not really done unless we wait a bit longer... (will get an access erorr trying to delete the dir)
timeout 20
:Delete the temp dir we were just using
rmdir %APP_NAME%Temp%CLONE_DIR_POSTFIX% /S /Q
pause
exit

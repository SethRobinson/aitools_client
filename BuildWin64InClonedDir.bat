:This builds the win version in a temp dir, but then copies back the final data to the real dir (build/win dir)
SET BUILDMODE=RELEASE

:Setting no pause causes our .bat files to not do pause commands when they are done
:set NO_PAUSE=1
:First, let's customize the directory name we're going to close to, by adding a postfix to make it unique
SET CLONE_DIR_POSTFIX=Win64
:Now let's actually make it, we'll pass in the postfix as a parm
call CloneToTempDir.bat %CLONE_DIR_POSTFIX%

:Move to build dir
cd ../%APP_NAME%Temp%CLONE_DIR_POSTFIX%
:Do the actual build
call BuildWin64.bat
:destroy any old windows builds in our main dir
rmdir ..\%APP_NAME%\build\win /S /Q
:recreate the dir
mkdir ..\%APP_NAME%\build\win
:copy the final build back to our main dir
xcopy build\win ..\%APP_NAME%\build\win /E /F /Y

:copy the zip too
del ..\%APP_NAME%\%ZIP_FNAME%
copy %ZIP_FNAME% ..\%APP_NAME%
:Move back out of it
cd ..
:Delete the temp dir we were just using
rmdir %APP_NAME%Temp%CLONE_DIR_POSTFIX% /S /Q
:move back into the main dir
cd %APP_NAME%
if "%NO_PAUSE%"=="" pause

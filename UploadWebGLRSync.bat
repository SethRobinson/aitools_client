call app_info_setup.bat
SET FILENAME=%APP_NAME%

REM change to correct dir, needed hack to run as admin for this to work
cd /D "%~dp0"

:just in case we need to make this dir...
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "mkdir ~/www/%WEB_SUB_DIR%"

if "%BUILDMODE%"=="RELEASE" (
:delete entire old dir.  Not using %APP_NAME% for safety
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "rm -rf ~/www/%WEB_SUB_DIR%/Build"
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "rm -rf ~/www/%WEB_SUB_DIR%/TemplateData"

:--chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r 
wsl rsync -avzr --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r  -e "ssh" webgltemp/%APP_NAME%/* %_FTP_USER_%@%_FTP_SITE_%:www/%WEB_SUB_DIR%

echo Files synced uploaded:  https://www.%_FTP_SITE_%/%WEB_SUB_DIR%

:Let's go ahead an open a browser to test it
start https://www.%_FTP_SITE_%/%WEB_SUB_DIR%

) else (

:just in case we need to make this dir...
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "mkdir ~/www/%WEB_SUB_DIR%/beta

:delete entire old dir.  Not using %APP_NAME% for safety
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "rm -rf ~/www/%WEB_SUB_DIR%beta/Build"
wsl ssh %_FTP_USER_%@%_FTP_SITE_% "rm -rf ~/www/%WEB_SUB_DIR%beta/TemplateData"

:--chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r 
wsl rsync -avzr --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r  -e "ssh" webgltemp/%APP_NAME%/* %_FTP_USER_%@%_FTP_SITE_%:www/%WEB_SUB_DIR%/beta

echo Files synced uploaded:  https://www.%_FTP_SITE_%/%WEB_SUB_DIR%/beta

:Let's go ahead an open a browser to test it
start https://www.%_FTP_SITE_%/%WEB_SUB_DIR%/beta

)


:Let's run unity again too?
:%UNITY_EXE%
if "%NO_PAUSE%"=="" pause

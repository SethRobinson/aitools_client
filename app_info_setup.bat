if "%~dp0"=="" exit /b 1
if not exist "%~dp0..\base_setup.bat" exit /b 1
call "%~dp0..\base_setup.bat"
if errorlevel 1 exit /b 1
SET APP_NAME=aitools_client
SET "APP_PATH=%~dp0"
:Package names are used in Android builds.  It needs to match the Unity project setting
SET APP_PACKAGE_NAME=com.rtsoft.%APP_NAME%

:If website file transfer batch files are used, these should be set here (or in their .bats)
set _FTP_USER_=toolfish
set _FTP_SITE_=toolfish.com
SET WEB_SUB_DIR=aitools

:Applicable if UploadLinux64HeadlessRSync.bat and friends are used (note: "Server" gets appended to the name as well, and "BetaServer" for beta versions)
SET LINUX_SERVER_BINARY_NAME=aitools

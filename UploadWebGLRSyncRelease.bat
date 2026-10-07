@echo off
setlocal
SET BUILDMODE=RELEASE
call "%~dp0UploadWebGLRSync.bat"
exit /b %errorlevel%

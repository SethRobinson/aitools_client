:Add a few more files we need
mkdir build
mkdir build\win
mkdir build\win\utils
mkdir build\win\output

xcopy /c utils build\win\utils\ /E /F /Y
xcopy /c web build\win\web\ /E /F /Y
xcopy /c Adventure build\win\Adventure\ /E /F /Y
xcopy /c AIGuide build\win\AIGuide\ /E /F /Y
xcopy /c ComfyUI build\win\ComfyUI\ /E /F /Y
xcopy /c Presets build\win\Presets\ /E /F /Y
copy config.txt build\win
copy config_llm.txt build\win
copy config_cam.txt build\win
copy config_preferences.txt build\win
pause
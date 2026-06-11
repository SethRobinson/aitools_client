@echo off
rem Windows launcher for aitools_cli.py. First run creates a local venv and
rem installs requirements; later runs go straight to the script. Safe to call
rem from any directory (output paths resolve against the caller's cwd).
setlocal

set "VENV=%~dp0venv"
set "PYEXE=%VENV%\Scripts\python.exe"
set "REQS=%~dp0requirements.txt"
set "INSTALLED=%VENV%\installed_requirements.txt"

if not exist "%PYEXE%" (
    echo Setting up Python environment in "%VENV%"...
    where py >nul 2>nul
    if not errorlevel 1 (
        py -3 -m venv "%VENV%"
    ) else (
        where python >nul 2>nul
        if errorlevel 1 (
            echo ERROR: No Python found. Install Python 3 from https://www.python.org/ and re-run.
            exit /b 1
        )
        python -m venv "%VENV%"
    )
    if not exist "%PYEXE%" (
        echo ERROR: Failed to create the virtual environment.
        exit /b 1
    )
)

rem (Re)install deps if never installed or requirements.txt changed.
set NEED_INSTALL=0
if not exist "%INSTALLED%" set NEED_INSTALL=1
if exist "%INSTALLED%" fc /b "%REQS%" "%INSTALLED%" >nul 2>nul || set NEED_INSTALL=1
if %NEED_INSTALL%==1 (
    echo Installing Python dependencies...
    "%PYEXE%" -m pip install --quiet --disable-pip-version-check -r "%REQS%"
    if errorlevel 1 (
        echo ERROR: pip install failed.
        exit /b 1
    )
    copy /y "%REQS%" "%INSTALLED%" >nul
)

"%PYEXE%" "%~dp0aitools_cli.py" %*
exit /b %errorlevel%

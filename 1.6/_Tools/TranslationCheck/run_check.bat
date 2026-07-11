@echo off
REM ---------------------------------------------------------------------------
REM  Translation-key missing checker launcher (Fortified Feature Framework)
REM  Double-click to scan Languages/ and produce MissingTranslations.txt.
REM ---------------------------------------------------------------------------
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "SCRIPT=%~dp0check_translations.py"

REM Locate a Python launcher: prefer the "py" launcher, then "python".
set "PY="
where py >nul 2>nul && set "PY=py"
if not defined PY (
    where python >nul 2>nul && set "PY=python"
)

if not defined PY (
    echo.
    echo ERROR: Python was not found on this system.
    echo Install Python 3 from https://www.python.org/ and re-run.
    echo.
    pause
    exit /b 1
)

echo Running translation check with "%PY%"...
echo.
"%PY%" "%SCRIPT%"
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
    REM Open the report in the default text editor.
    if exist "%~dp0MissingTranslations.txt" start "" "%~dp0MissingTranslations.txt"
) else (
    echo Script exited with code %RC%.
)

echo.
pause
endlocal

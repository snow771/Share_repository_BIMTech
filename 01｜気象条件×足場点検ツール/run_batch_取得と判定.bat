@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Fetch and judge (run from Task Scheduler)
rem ============================================================
rem  chcp 65001 is required before any Japanese text.
rem  cmd reads this file as Shift_JIS by default and would
rem  break lines at certain Japanese characters.
rem ============================================================

cd /d "%~dp0"

rem  Use appsettings.Production.json (the shared PC database).
rem
rem  Without this line the app would read appsettings.json, which
rem  points at the development PC. Re-deploying would then send the
rem  data to the wrong database without anyone noticing.
set DOTNET_ENVIRONMENT=Production

set LOGDIR=%~dp0logs
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

set LOGFILE=%LOGDIR%\batch_%DATE:~0,4%%DATE:~5,2%%DATE:~8,2%.log

echo. >> "%LOGFILE%"
echo ============================================================ >> "%LOGFILE%"
echo %DATE% %TIME% >> "%LOGFILE%"
echo ============================================================ >> "%LOGFILE%"

"%~dp0ScaffoldAlert.Batch.exe" >> "%LOGFILE%" 2>&1

if errorlevel 1 (
    echo [NG] batch failed. See "%LOGFILE%"
    exit /b 1
)

exit /b 0

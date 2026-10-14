@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Remove the web app service  (run as administrator)
rem ============================================================

set SERVICENAME=ScaffoldAlert

sc stop "%SERVICENAME%" > nul 2>&1
timeout /t 3 /nobreak > nul
sc delete "%SERVICENAME%"

if errorlevel 1 (
    echo.
    echo [NG] Could not remove the service. It may not exist.
    echo.
    pause
    exit /b 1
)

echo.
echo Removed: %SERVICENAME%
echo.
pause
exit /b 0

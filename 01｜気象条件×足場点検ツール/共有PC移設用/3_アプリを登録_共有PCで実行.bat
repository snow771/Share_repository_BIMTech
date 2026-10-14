@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Register the web app as a Windows service
rem  Run this AS ADMINISTRATOR on the shared PC.
rem ============================================================
rem  As a service it runs without anyone logged in, and comes
rem  back automatically after the PC restarts.
rem ============================================================

set SERVICENAME=ScaffoldAlert
set EXE=%~dp0ScaffoldAlert.Web.exe

if not exist "%EXE%" (
    echo [NG] ScaffoldAlert.Web.exe not found next to this file.
    echo      Put this file in the same folder as the web app.
    echo.
    pause
    exit /b 1
)

rem --- Remove the old one if it is already there ---
sc query "%SERVICENAME%" > nul 2>&1
if not errorlevel 1 (
    echo Stopping the existing service...
    sc stop "%SERVICENAME%" > nul 2>&1
    timeout /t 3 /nobreak > nul
    sc delete "%SERVICENAME%" > nul 2>&1
    timeout /t 2 /nobreak > nul
)

sc create "%SERVICENAME%" binPath= "\"%EXE%\"" start= auto ^
    DisplayName= "ScaffoldAlert (足場点検義務管理)"
if errorlevel 1 (
    echo.
    echo [NG] Could not create the service.
    echo      Right click this file and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

rem --- Restart automatically if it stops unexpectedly ---
sc failure "%SERVICENAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000

sc description "%SERVICENAME%" "足場点検義務管理ツールの画面。ブラウザから http://この端末名:5080/ で開きます。"

sc start "%SERVICENAME%"
if errorlevel 1 (
    echo.
    echo [NG] The service was created but did not start.
    echo      See Event Viewer, or run ScaffoldAlert.Web.exe by hand
    echo      to see the reason.
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Registered and started: %SERVICENAME%
echo.
echo  Open from other PCs:
echo    http://%COMPUTERNAME%:5080/
echo.
echo  If it does not open, allow port 5080 through the firewall
echo  with 4_ポートを開ける_共有PCで実行.bat
echo ============================================================
echo.
pause
exit /b 0

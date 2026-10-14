@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Allow port 5080 through Windows Firewall
rem  Run this AS ADMINISTRATOR on the shared PC.
rem ============================================================
rem  Without this, other PCs cannot open the app even though
rem  the service is running.
rem ============================================================

set RULENAME=ScaffoldAlert 5080

netsh advfirewall firewall delete rule name="%RULENAME%" > nul 2>&1

netsh advfirewall firewall add rule name="%RULENAME%" ^
    dir=in action=allow protocol=TCP localport=5080 profile=domain,private
if errorlevel 1 (
    echo.
    echo [NG] Could not add the firewall rule.
    echo      Right click this file and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Allowed TCP 5080 (domain and private networks)
echo  Public networks are NOT allowed on purpose.
echo ============================================================
echo.
pause
exit /b 0

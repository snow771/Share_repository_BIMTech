@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Remove the hourly task  (run as administrator)
rem ============================================================

set TASKNAME=ScaffoldAlert_取得と判定

schtasks /delete /tn "%TASKNAME%" /f
if errorlevel 1 (
    echo.
    echo [NG] Could not remove the task. It may not exist.
    echo.
    pause
    exit /b 1
)

echo.
echo Removed: %TASKNAME%
echo.
pause
exit /b 0

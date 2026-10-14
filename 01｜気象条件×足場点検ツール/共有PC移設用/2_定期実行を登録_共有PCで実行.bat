@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Register the scheduled task on the shared PC
rem  Run this AS ADMINISTRATOR on the shared PC.
rem ============================================================
rem  Put this file in the SAME folder as ScaffoldAlert.Batch.exe
rem  and run_batch_取得と判定.bat
rem ============================================================


rem ============================================================
rem  SETTINGS - change these two lines if you need to
rem ============================================================
rem
rem  STARTTIME  The first run of the day, in 24h "HH:MM".
rem
rem      "00:10" is the default. JMA publishes the hourly
rem      observation a few minutes after the hour, so starting
rem      at :10 (not :00) picks up fresh data every time.
rem
rem      With INTERVAL=1 the task then runs at
rem      00:10, 01:10, 02:10 ... 23:10 every day.
rem
rem  INTERVAL   Hours between runs.
rem
rem      1 = every hour (default)
rem      2 = every 2 hours
rem      3 = every 3 hours
rem
rem      The judgement uses a 12-hour rainfall total, so once an
rem      hour is enough. Running more often does not help: JMA
rem      only updates the values once an hour.
rem
rem ============================================================

set TASKNAME=ScaffoldAlert_取得と判定
set STARTTIME=14:57
set INTERVAL=1

rem ============================================================


set TARGET=%~dp0run_batch_取得と判定.bat

rem --- Make sure we are next to the batch program ---
if not exist "%~dp0ScaffoldAlert.Batch.exe" (
    echo [NG] ScaffoldAlert.Batch.exe not found next to this file.
    echo      Put this file in the same folder as the batch program.
    echo.
    pause
    exit /b 1
)

if not exist "%TARGET%" (
    echo [NG] run_batch_取得と判定.bat not found next to this file.
    echo      Copy it from the project folder.
    echo.
    pause
    exit /b 1
)

rem --- Check the database first ---
rem  Registering a task that cannot reach the database only
rem  produces failures every hour. Fail here instead.
rem
rem  DOTNET_ENVIRONMENT=Production makes the program read
rem  appsettings.Production.json (the shared PC database),
rem  which is what the scheduled task will use.
rem  Without this line the check would look at the development
rem  PC instead and fail here for the wrong reason.
echo Checking the database...
setlocal
set DOTNET_ENVIRONMENT=Production
"%~dp0ScaffoldAlert.Batch.exe" fetch > "%~dp0check.log" 2>&1
set CHECKRESULT=%errorlevel%
endlocal & set CHECKRESULT=%CHECKRESULT%

if not "%CHECKRESULT%"=="0" (
    echo.
    echo [NG] The batch program could not run.
    echo.
    echo      See what happened:  check.log  ^(next to this file^)
    echo.
    type "%~dp0check.log"
    echo.
    pause
    exit /b 1
)

del "%~dp0check.log" > nul 2>&1
echo   OK

rem --- Register ---
rem  /sc hourly /mo N  ... every N hours
rem  /st HH:MM         ... first run of the day
rem  /ru SYSTEM        ... runs with nobody logged in
rem  /rl HIGHEST       ... highest privileges
rem  /f                ... replace an existing task with this name
schtasks /create /tn "%TASKNAME%" /tr "\"%TARGET%\"" ^
    /sc hourly /mo %INTERVAL% /st %STARTTIME% ^
    /ru SYSTEM /rl HIGHEST /f
if errorlevel 1 (
    echo.
    echo [NG] Could not register the task.
    echo      Right click this file and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

rem --- Show what was registered ---
echo.
echo ============================================================
echo  Registered: %TASKNAME%
echo    First run : %STARTTIME%
echo    Interval  : every %INTERVAL% hour(s)
echo    Account   : SYSTEM (runs with nobody logged in)
echo ============================================================
echo.

schtasks /query /tn "%TASKNAME%" /fo LIST

echo.
echo  To run it right now (a test):
echo     schtasks /run /tn "%TASKNAME%"
echo.
echo  To change the time later, edit STARTTIME / INTERVAL at the
echo  top of this file and run it again. /f replaces the old one.
echo.
pause
exit /b 0

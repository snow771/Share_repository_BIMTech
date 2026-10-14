@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Publish ONLY the scheduled-fetch program, as ONE file
rem ============================================================
rem  Why one file:
rem    A normal self-contained publish produces about 200 files.
rem    The Visual Studio Setup Project handles files one by one,
rem    so 200 files makes the build take hours.
rem
rem    PublishSingleFile packs everything into a single .exe.
rem    The Setup Project then has only a handful of files and
rem    builds in seconds.
rem
rem  Output:  共有PC移設用\publish\batch-single\
rem ============================================================

set OUT=%~dp0publish\batch-single

cd /d "%~dp0.."

echo.
echo Publishing the batch program as a single file...
echo (this takes a few minutes)
echo.

dotnet publish ScaffoldAlert.Batch\ScaffoldAlert.Batch.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none ^
    -o "%OUT%"
if errorlevel 1 goto failed

rem  The launcher the scheduled task calls.
rem
rem  Renamed to ASCII on purpose. The installer hands this path
rem  to schtasks, and a Japanese file name there depends on the
rem  PC code page. ASCII removes that risk.
rem  The contents are identical to the originals.
copy /y "%~dp0..\run_batch_取得と判定.bat" "%OUT%\run_batch.bat" > nul
copy /y "%~dp0..\run_batch_手動で試す.bat" "%OUT%\run_batch_test.bat" > nul

echo.
echo ============================================================
echo  Done.  %OUT%
echo.
dir /b "%OUT%"
echo.
echo  These are ALL the files needed. Nothing else.
echo ============================================================
echo.
pause
exit /b 0

:failed
echo.
echo [NG] Publish failed. See the messages above.
echo.
pause
exit /b 1

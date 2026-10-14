@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Build and publish for the shared PC
rem ============================================================
rem  Run this on the PC that has Visual Studio.
rem  Output goes to  publish\web  and  publish\batch
rem
rem  --self-contained true  ... bundle .NET itself
rem
rem  Why bundle: without it, the shared PC needs the ASP.NET Core
rem  Runtime installed. That needs an administrator and one more
rem  approval from IT. Bundling makes each folder bigger
rem  (about 100 MB) but nothing has to be installed on the
rem  shared PC. Disk space only - it does not run any slower.
rem ============================================================

cd /d "%~dp0.."

echo.
echo [1/2] Web app
dotnet publish ScaffoldAlert.Web\ScaffoldAlert.Web.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -o "%~dp0publish\web"
if errorlevel 1 goto failed

echo.
echo [2/2] Batch
dotnet publish ScaffoldAlert.Batch\ScaffoldAlert.Batch.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -o "%~dp0publish\batch"
if errorlevel 1 goto failed

copy /y "%~dp0..\run_batch_取得と判定.bat" "%~dp0publish\batch\" > nul
copy /y "%~dp0..\run_batch_手動で試す.bat" "%~dp0publish\batch\" > nul

echo.
echo ============================================================
echo  Done.
echo    publish\web    ... copy to the shared PC
echo    publish\batch  ... copy to the shared PC
echo.
echo  .NET is bundled, so the shared PC needs no install.
echo  Each folder is about 100 MB.
echo ============================================================
echo.
pause
exit /b 0

:failed
echo.
echo [NG] publish failed. See the messages above.
echo.
pause
exit /b 1

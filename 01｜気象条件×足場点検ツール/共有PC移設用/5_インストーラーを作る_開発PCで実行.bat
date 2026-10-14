@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Build the installer (.msi and setup.exe)
rem ============================================================
rem  Run this on the PC that has Visual Studio.
rem
rem  It does everything in the right order:
rem    1. publish the web app
rem    2. publish the batch program
rem    3. copy the launcher bat files
rem    4. build ScaffoldAlert.msi
rem    5. build ScaffoldAlert.exe  (the setup.exe)
rem
rem  Doing it by hand is easy to get wrong: if you build the .msi
rem  without publishing first, the .msi quietly contains the OLD
rem  application. It looks fine and installs fine.
rem
rem  Output:  共有PC移設用\installer\
rem ============================================================

set OUT=%~dp0installer

cd /d "%~dp0.."

echo.
echo [1/5] Publishing the web app...
dotnet publish ScaffoldAlert.Web\ScaffoldAlert.Web.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -o "%~dp0publish\web"
if errorlevel 1 goto failed

echo.
echo [2/5] Publishing the batch program...
dotnet publish ScaffoldAlert.Batch\ScaffoldAlert.Batch.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -o "%~dp0publish\batch"
if errorlevel 1 goto failed

echo.
echo [3/5] Copying the launcher files...
copy /y "%~dp0..\run_batch_取得と判定.bat" "%~dp0publish\batch\" > nul
copy /y "%~dp0..\run_batch_手動で試す.bat" "%~dp0publish\batch\" > nul
if errorlevel 1 goto failed

rem  This file is only for the shared PC. Do not ship it.
if exist "%~dp0publish\web\appsettings.Demo.json" (
    del "%~dp0publish\web\appsettings.Demo.json"
)

echo.
echo [4/5] Building ScaffoldAlert.msi...
dotnet build ScaffoldAlert.Installer\ScaffoldAlert.Installer.wixproj ^
    -c Release
if errorlevel 1 goto failed_wix

echo.
echo [5/5] Building setup.exe...
dotnet build ScaffoldAlert.Bundle\ScaffoldAlert.Bundle.wixproj ^
    -c Release
if errorlevel 1 goto failed_wix

rem --- Collect the two files in one place ---
if not exist "%OUT%" mkdir "%OUT%"
copy /y "ScaffoldAlert.Installer\bin\Release\ScaffoldAlert.msi" "%OUT%\" > nul
copy /y "ScaffoldAlert.Bundle\bin\Release\ScaffoldAlert.exe" "%OUT%\setup.exe" > nul

echo.
echo ============================================================
echo  Done.
echo.
echo    %OUT%\ScaffoldAlert.msi
echo    %OUT%\setup.exe
echo.
echo  TEST THESE ON YOUR OWN PC BEFORE HANDING THEM OVER.
echo  See A2_インストーラーの作り方.md, step 4.
echo ============================================================
echo.
pause
exit /b 0

:failed_wix
echo.
echo [NG] The installer build failed.
echo.
echo  If the message mentions "wix" or "WixToolset", the WiX
echo  tools are probably missing. Install them once with:
echo.
echo     dotnet tool install --global wix
echo.
echo  Then run this file again.
echo.
pause
exit /b 1

:failed
echo.
echo [NG] Build failed. See the messages above.
echo.
pause
exit /b 1

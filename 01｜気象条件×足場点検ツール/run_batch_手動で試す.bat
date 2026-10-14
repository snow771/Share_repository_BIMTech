@echo off
chcp 65001 > nul
setlocal

rem ============================================================
rem  Run once and keep the window open (for checking by hand)
rem ============================================================

cd /d "%~dp0"

"%~dp0ScaffoldAlert.Batch.exe"

echo.
pause

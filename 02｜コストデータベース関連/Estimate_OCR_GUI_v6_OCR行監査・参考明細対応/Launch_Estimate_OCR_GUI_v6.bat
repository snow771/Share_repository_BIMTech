@echo off
setlocal
cd /d "%~dp0"
python "%~dp0estimate_ocr_gui_v6.py"
if errorlevel 1 (
  echo.
  echo GUI START ERROR. Copy this screen and send it to ChatGPT.
)
pause

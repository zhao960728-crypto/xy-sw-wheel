@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "code=%ERRORLEVEL%"
echo.
if not "%code%"=="0" (
  echo Install failed. Please check the message above.
) else (
  echo Install completed.
)
pause
exit /b %code%

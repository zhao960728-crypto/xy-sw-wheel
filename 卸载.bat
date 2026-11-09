@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
set "code=%ERRORLEVEL%"
echo.
if not "%code%"=="0" (
  echo Uninstall failed. Please check the message above.
) else (
  echo Uninstall completed.
)
pause
exit /b %code%

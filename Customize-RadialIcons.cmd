@echo off
set "RadialIconScript=%~dp0Customize-RadialIcons.ps1"
if not exist "%RadialIconScript%" set "RadialIconScript=%~dp0tools\Customize-RadialIcons.ps1"
powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%RadialIconScript%"

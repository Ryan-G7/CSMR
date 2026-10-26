@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-Presentation.ps1" -Autoplay -Export %*
if errorlevel 1 pause

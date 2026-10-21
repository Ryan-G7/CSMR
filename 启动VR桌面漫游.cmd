@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-VR.ps1" -Desktop
if errorlevel 1 pause
endlocal

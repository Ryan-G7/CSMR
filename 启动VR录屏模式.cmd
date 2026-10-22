@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-VR.ps1" -Recording
if errorlevel 1 pause
endlocal

@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-Review.ps1" -Mode VR
if errorlevel 1 pause
endlocal

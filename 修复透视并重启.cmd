@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-HuijingMR.ps1" -RestartCameraBackend
if errorlevel 1 (
    echo.
    echo MR camera recovery failed. See the error above.
    pause
    exit /b 1
)
endlocal

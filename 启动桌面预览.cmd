@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CarAR\Launch\Start-DesktopPreview.ps1"
if errorlevel 1 (
    echo.
    echo Preview startup failed. See the error above.
    pause
    exit /b 1
)
endlocal

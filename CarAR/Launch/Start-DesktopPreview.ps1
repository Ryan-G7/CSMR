param()
$ErrorActionPreference = 'Stop'
$application = & (Join-Path $PSScriptRoot 'Get-CarARPlayer.ps1')
Start-Process -FilePath $application -ArgumentList '-force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900' -WorkingDirectory (Split-Path $application)

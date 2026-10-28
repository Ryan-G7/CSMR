param([switch]$MR)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$application = Join-Path $project 'Build/Windows/CarAR.exe'
$capabilities = Join-Path $project 'Build/Windows/carar-capabilities.json'
if (-not (Test-Path -LiteralPath $application) -or -not (Test-Path -LiteralPath $capabilities)) {
    throw 'This Varjo mode requires the new local player. Run CarAR/Tools/Build-Windows.ps1 -Smoke first.'
}
$features = Get-Content -LiteralPath $capabilities -Raw | ConvertFrom-Json
if (($MR -and -not $features.varjoMr) -or (-not $MR -and -not $features.varjoVr)) {
    throw 'The installed player has no Varjo support. Rebuild with CarAR/Tools/Build-Windows.ps1.'
}
$running = Get-Process CarAR -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $application }
if ($running) { throw 'CarAR is already running. Exit it before switching XR devices or modes.' }
$mode = if ($MR) { '--varjo-mr' } else { '--varjo' }
$log = Join-Path $project $(if ($MR) { 'varjo-mr.log' } else { 'varjo-vr.log' })
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $application
$start.WorkingDirectory = Split-Path $application
$start.Arguments = "$mode -force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -logFile `"$log`""
$start.UseShellExecute = $false
foreach ($key in @('XR_ENABLE_API_LAYERS', 'XR_API_LAYER_PATH', 'XR_RUNTIME_JSON', 'CARAR_HJXR_SDK')) { $start.EnvironmentVariables.Remove($key) }
$start.EnvironmentVariables['DISABLE_XR_APILAYER_HJMR_passthrough'] = '1'
$process = [System.Diagnostics.Process]::Start($start)
Write-Output "CarAR Varjo $(if ($MR) { 'MR' } else { 'VR' }) started (PID $($process.Id)). Log: $log"
Write-Output 'Use paired Index/Vive controllers; Menu / Y / B opens the review panel.'

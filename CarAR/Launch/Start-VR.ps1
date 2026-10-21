param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$application = Join-Path $project 'Build/Windows/CarAR.exe'
$capabilities = Join-Path $project 'Build/Windows/carar-capabilities.json'
if (-not (Test-Path -LiteralPath $application) -or -not (Test-Path -LiteralPath $capabilities)) {
    throw 'This VR scene requires the new local player. Run CarAR/Tools/Build-Windows.ps1 -Smoke first.'
}
$features = Get-Content -LiteralPath $capabilities -Raw | ConvertFrom-Json
if (-not $features.vr) { throw 'The installed player has no VR room. Rebuild with CarAR/Tools/Build-Windows.ps1.' }
$mode = if ($Desktop) { '--vr-desktop' } else { '--vr' }
$log = Join-Path $project $(if ($Desktop) { 'vr-desktop.log' } else { 'vr-headset.log' })
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $application
$start.WorkingDirectory = Split-Path $application
$start.Arguments = "$mode -force-d3d11 -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -logFile `"$log`""
$start.UseShellExecute = $false
# Do not inherit an MR-only compositor layer from a previous diagnostic shell.
foreach ($key in @('XR_ENABLE_API_LAYERS', 'XR_API_LAYER_PATH', 'CARAR_HJXR_SDK')) { $start.EnvironmentVariables.Remove($key) }
# The vendor also registers this layer implicitly; pure VR must opt out.
$start.EnvironmentVariables['DISABLE_XR_APILAYER_HJMR_passthrough'] = '1'
[void][System.Diagnostics.Process]::Start($start)

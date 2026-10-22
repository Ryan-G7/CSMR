param([switch]$Desktop, [switch]$Recording)
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
$log = Join-Path $project $(if ($Recording) { 'vr-recording.log' } elseif ($Desktop) { 'vr-desktop.log' } else { 'vr-headset.log' })
$running = Get-Process CarAR -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $application }
if ($running) { throw 'CarAR is already running. Exit it first, or press F8 in the VR window to toggle recording optimization.' }
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $application
$start.WorkingDirectory = Split-Path $application
$width = if ($Recording) { 1920 } else { 1440 }
$height = if ($Recording) { 1080 } else { 900 }
$recordingArgument = if ($Recording) { ' --vr-recording' } else { '' }
$start.Arguments = "$mode$recordingArgument -force-d3d11 -screen-fullscreen 0 -screen-width $width -screen-height $height -logFile `"$log`""
$start.UseShellExecute = $false
# Do not inherit an MR-only compositor layer from a previous diagnostic shell.
foreach ($key in @('XR_ENABLE_API_LAYERS', 'XR_API_LAYER_PATH', 'CARAR_HJXR_SDK')) { $start.EnvironmentVariables.Remove($key) }
# The vendor also registers this layer implicitly; pure VR must opt out.
$start.EnvironmentVariables['DISABLE_XR_APILAYER_HJMR_passthrough'] = '1'
[void][System.Diagnostics.Process]::Start($start)

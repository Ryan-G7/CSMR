param(
    [string]$AgentRoot = 'D:\Soft\AIXR Agent',
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\Build\Windows\CarAR.exe'),
    [switch]$UseSystemRuntime,
    [switch]$NoStartSensors,
    [switch]$NoStartServer
)
$ErrorActionPreference = 'Stop'
$layerDir = Join-Path $AgentRoot 'resource\tool\api_layer\Release'
$sdkPath = Join-Path $layerDir 'jxrsdk.dll'
$manifest = Join-Path $layerDir 'openxr-api-layer.json'
$runtime = Join-Path $AgentRoot 'resource\tool\openxr_runtime\Release\pimax-openxr.json'
foreach ($required in @($ApplicationPath, $sdkPath, $manifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing required file: $required" }
}
if (-not $UseSystemRuntime -and -not (Test-Path -LiteralPath $runtime -PathType Leaf)) { throw "Missing manufacturer runtime: $runtime" }
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $NoStartSensors) {
    $sensorDir = Join-Path $env:ProgramFiles 'Inuitive\InuDev\bin'
    foreach ($sensorName in @('InuService1', 'InuService2')) {
        $sensorExe = Join-Path $sensorDir ($sensorName + '.exe')
        if (Test-Path -LiteralPath $sensorExe -PathType Leaf) {
            if (-not (Get-Process $sensorName -ErrorAction SilentlyContinue)) {
                # The manufacturer uses these two console processes. The disabled Windows InuService stays unchanged.
                Start-Process -FilePath $sensorExe -ArgumentList 'console 1' -WorkingDirectory $sensorDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectDir ($sensorName + '.log')) -RedirectStandardError (Join-Path $projectDir ($sensorName + '.error.log')) | Out-Null
            }
        } else { Write-Warning "Missing sensor backend: $sensorExe. Install the matching manufacturer driver package." }
    }
}
if (-not $NoStartServer -and -not (Get-Process hjxr_server -ErrorAction SilentlyContinue)) {
    $server = Join-Path $layerDir 'hjxr_server.exe'
    Start-Process -FilePath $server -WorkingDirectory $layerDir -WindowStyle Hidden | Out-Null
}
$logPath = Join-Path $projectDir 'huijing-mr.log'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = [IO.Path]::GetFullPath($ApplicationPath)
$psi.WorkingDirectory = [IO.Path]::GetDirectoryName($psi.FileName)
$psi.UseShellExecute = $false
$psi.Arguments = '--hjxr -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -logFile "' + $logPath + '"'
# Only this child process gets the vendor layer/runtime. Do not write the OpenXR registry or replace installed DLLs.
$psi.EnvironmentVariables['CARAR_HJXR_SDK'] = $sdkPath
$psi.EnvironmentVariables['XR_API_LAYER_PATH'] = $layerDir
$psi.EnvironmentVariables['XR_ENABLE_API_LAYERS'] = 'XR_APILAYER_HJMR_passthrough'
$psi.EnvironmentVariables.Remove('DISABLE_XR_APILAYER_HJMR_passthrough')
if (-not $UseSystemRuntime) { $psi.EnvironmentVariables['XR_RUNTIME_JSON'] = $runtime }
$appProcess = [System.Diagnostics.Process]::Start($psi)
Write-Output "CarAR Huijing MR started (PID $($appProcess.Id)). Log: $logPath"
Write-Output 'Wear the headset, then press controller Menu / Y / B for the review panel. Use floor placement to park and lock the vehicle. K is optional marker alignment.'

param(
    [string]$AgentRoot = 'D:\Soft\AIXR Agent',
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\Build\Windows\CarAR.exe'),
    [switch]$UseSystemRuntime,
    [switch]$NoStartSensors,
    [switch]$NoStartServer,
    [switch]$RestartCameraBackend
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
$sensorDir = Join-Path $env:ProgramFiles 'Inuitive\InuDev\bin'
$sensorNames = @('InuService1', 'InuService2')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$startedSensorIds = @{}
function Get-SensorState([string]$Name) {
    $processes = @(Get-Process $Name -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return 'Stopped' }
    $path = Join-Path $projectDir ($Name + '.log')
    if (-not (Test-Path -LiteralPath $path)) { return 'Unknown' }
    # A process surviving USB removal is not evidence that its camera is connected.
    # Only use a log belonging to this process lifetime; other launchers may own the backend.
    if ($startedSensorIds[$Name] -ne $processes[0].Id -and
        (Get-Item -LiteralPath $path).LastWriteTime -lt $processes[0].StartTime.AddSeconds(-2)) { return 'Unknown' }
    # Get-Content shares access with the live stdout writer; ReadAllText locks it out.
    try { $content = [string](Get-Content -LiteralPath $path -Raw -ErrorAction Stop) } catch { return 'Unknown' }
    $events = [regex]::Matches($content, 'Module is ready to work|Module stopped working|INU_DEVICE__DISCONNECT|BootControl is invalid|INU_DEFSG_USB_EVT_REMOVAL')
    if ($events.Count -eq 0) { return 'Unknown' }
    if ($events[$events.Count - 1].Value -eq 'Module is ready to work') { return 'Ready' }
    return 'Disconnected'
}
function Save-LaunchLog([string]$Name) {
    $path = Join-Path $projectDir $Name
    if (Test-Path -LiteralPath $path) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $projectDir ("before-mr-start-$stamp-$Name"))
    }
}
function Stop-BackendProcess([System.Diagnostics.Process]$Process) {
    if ($Process.HasExited) { return }
    $Process.Kill()
    # Termination is asynchronous. Rechecking names immediately can still find the
    # dying process and skip its replacement, leaving the new MR client without cameras.
    if (-not $Process.WaitForExit(5000)) { throw "Process did not exit: $($Process.ProcessName) ($($Process.Id))" }
}
if ($RestartCameraBackend -and ($NoStartSensors -or $NoStartServer)) {
    throw '-RestartCameraBackend requires both camera and MR server management.'
}
if (-not $NoStartSensors) {
    foreach ($sensorName in $sensorNames) {
        $sensorExe = Join-Path $sensorDir ($sensorName + '.exe')
        if (-not (Test-Path -LiteralPath $sensorExe -PathType Leaf)) { throw "Missing sensor backend: $sensorExe. Install the matching manufacturer driver package." }
        if ((Get-SensorState $sensorName) -eq 'Disconnected') { $RestartCameraBackend = $true }
    }
}
if ($RestartCameraBackend -and $NoStartServer) { throw 'A disconnected camera needs an MR server restart. Remove -NoStartServer to recover.' }
$applicationFullPath = [IO.Path]::GetFullPath($ApplicationPath)
$runningApps = @(Get-Process CarAR -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $applicationFullPath })
if ($RestartCameraBackend) {
    Write-Output 'Restarting the camera backend and MR session...'
    foreach ($process in $runningApps) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(8000)) { Stop-BackendProcess $process }
    }
    foreach ($process in @(Get-Process hjxr_server -ErrorAction SilentlyContinue)) { Stop-BackendProcess $process }
    foreach ($sensorName in $sensorNames) {
        foreach ($process in @(Get-Process $sensorName -ErrorAction SilentlyContinue)) { Stop-BackendProcess $process }
    }
} elseif ($runningApps.Count -gt 0) {
    Write-Output "CarAR is already running (PID $($runningApps[0].Id)). No second instance started."
    Write-Output 'For missing passthrough, close CarAR and run this script with -RestartCameraBackend.'
    return
}
if (-not $NoStartSensors) {
    $startedSensors = @()
    foreach ($sensorName in $sensorNames) {
        $sensorExe = Join-Path $sensorDir ($sensorName + '.exe')
        if (-not (Get-Process $sensorName -ErrorAction SilentlyContinue)) {
            Save-LaunchLog ($sensorName + '.log')
            Save-LaunchLog ($sensorName + '.error.log')
            # The manufacturer uses these two console processes. The disabled Windows InuService stays unchanged.
            $sensorProcess = Start-Process -FilePath $sensorExe -ArgumentList 'console 1' -WorkingDirectory $sensorDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectDir ($sensorName + '.log')) -RedirectStandardError (Join-Path $projectDir ($sensorName + '.error.log')) -PassThru
            $startedSensorIds[$sensorName] = $sensorProcess.Id
            $startedSensors += $sensorName
        } elseif ((Get-SensorState $sensorName) -eq 'Unknown') {
            Write-Warning "$sensorName exists, but its connection cannot be verified from this launcher's log. Use -RestartCameraBackend if passthrough is missing."
        }
    }
    # Wait for the actual USB connection callback before starting a client of the cameras.
    if ($startedSensors.Count -gt 0) {
        Write-Output 'Waiting for both camera backends to report ready (up to 30 seconds)...'
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            $notReady = @($startedSensors | Where-Object { (Get-SensorState $_) -ne 'Ready' })
            if ($notReady.Count -eq 0) { break }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($notReady.Count -gt 0) { throw "Camera not ready: $($notReady -join ', '). CarAR was not started. Check USB connection and $projectDir\InuService*.log, then retry." }
        Write-Output 'Both camera backends are ready.'
    }
}
if (-not $NoStartServer -and -not (Get-Process hjxr_server -ErrorAction SilentlyContinue)) {
    $server = Join-Path $layerDir 'hjxr_server.exe'
    Start-Process -FilePath $server -WorkingDirectory $layerDir -WindowStyle Hidden | Out-Null
}
$logPath = Join-Path $projectDir 'huijing-mr.log'
Save-LaunchLog 'huijing-mr.log'
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
Write-Output 'Wear the headset, then press controller Menu / Y / B for the review panel. Use floor placement to park and lock the vehicle.'

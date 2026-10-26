param([string]$UnityPath = $env:CARAR_UNITY_EDITOR, [switch]$Smoke, [switch]$Presentation)
$ErrorActionPreference = 'Stop'
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $UnityPath) { $UnityPath = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw 'Install Unity 6000.6.3f1 with Windows Build Support, or pass -UnityPath <Unity.exe>.'
}
# Portable exports are sufficient; the original Blender source is optional.
foreach ($asset in @('Assets/Models/AMG-interactive.glb', 'Assets/Environment/VR/CarRoom.glb')) {
    $source = [IO.File]::OpenRead((Join-Path $projectDir $asset))
    try {
        $prefix = New-Object byte[] 42
        [void]$source.Read($prefix, 0, $prefix.Length)
        if ([Text.Encoding]::ASCII.GetString($prefix).StartsWith('version https://git-lfs.github.com/spec/v1')) {
            throw 'Model assets are LFS pointers. Run git lfs install and git lfs pull.'
        }
    } finally { $source.Dispose() }
}
$log = Join-Path $projectDir $(if ($Presentation) { 'build-presentation.log' } else { 'build-windows.log' })
$method = if ($Presentation) { 'BuildPresentation' } else { 'BuildPreview' }
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $UnityPath
$start.WorkingDirectory = $projectDir
$start.Arguments = '-batchmode -force-d3d11 -disable-assembly-updater -job-worker-count 8 -projectPath "' + $projectDir + '" -executeMethod CarAR.EditorTools.CarARSetupWizard.' + $method + ' -logFile "' + $log + '"'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.EnvironmentVariables['DOTNET_SYSTEM_GLOBALIZATION_INVARIANT'] = '1'
$process = [System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity build failed ($($process.ExitCode)). See $log" }
$output = if ($Presentation) { 'Build/Presentation' } else { 'Build/Windows' }
$application = Join-Path $projectDir ($output + '/CarAR.exe')
if (-not (Test-Path -LiteralPath $application)) { throw 'Unity returned success without producing CarAR.exe.' }
# A new local build supersedes any previously downloaded player.
$stamp = Join-Path $projectDir ($output + '/carar-release.json')
if (Test-Path -LiteralPath $stamp) { Remove-Item -LiteralPath $stamp }
if ($Smoke) {
    $smokeLog = Join-Path $projectDir 'smoke-windows.log'
    $smokeArguments = @('--carar-smoke', '-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', ('"' + $smokeLog + '"'))
    # Native XRI visuals and frame capture require a visible render window.
    $process = Start-Process -FilePath $application -ArgumentList $smokeArguments -WorkingDirectory (Split-Path $application) -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Player regression failed ($($process.ExitCode)). See $smokeLog" }
    if ((Select-String -LiteralPath $smokeLog -Pattern '\[CarAR (MRTK Smoke|Controller Smoke|Review Regression|Smoke)\] PASS').Count -ne 4) {
        throw "Player exited without four PASS results. See $smokeLog"
    }
    foreach ($recording in @($false, $true)) {
        $vrLog = Join-Path $projectDir $(if ($recording) { 'smoke-vr-recording.log' } else { 'smoke-vr.log' })
        $vrArgs = @('--vr-smoke', '-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', ('"' + $vrLog + '"'))
        if ($recording) { $vrArgs += '--vr-recording' }
        $process = Start-Process -FilePath $application -ArgumentList $vrArgs -WorkingDirectory (Split-Path $application) -PassThru -Wait
        if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $vrLog -Pattern '\[CarAR VR Smoke\] PASS') -or -not (Select-String -LiteralPath $vrLog -Pattern '\[CarAR VR Recording Smoke\] PASS')) {
            throw "VR regression failed. See $vrLog"
        }
    }
}
Write-Host "Windows player ready: $application"

param([string]$UnityPath = $env:CARAR_UNITY_EDITOR, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $UnityPath) { $UnityPath = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw 'Install Unity 6000.6.3f1 with Windows Build Support, or pass -UnityPath <Unity.exe>.'
}
$source = [IO.File]::OpenRead((Join-Path $projectDir '../AMG.blend'))
try {
    $reader = New-Object IO.StreamReader($source)
    $prefix = New-Object char[] 42
    [void]$reader.Read($prefix, 0, $prefix.Length)
    if ((-join $prefix).StartsWith('version https://git-lfs.github.com/spec/v1')) {
        throw 'Model assets are LFS pointers. Run git lfs install and git lfs pull from the repository root.'
    }
} finally { $source.Dispose() }
$log = Join-Path $projectDir 'build-windows.log'
$arguments = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $projectDir + '"'), '-executeMethod', 'CarAR.EditorTools.CarARSetupWizard.BuildPreview', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Unity build failed ($($process.ExitCode)). See $log" }
$application = Join-Path $projectDir 'Build/Windows/CarAR.exe'
if (-not (Test-Path -LiteralPath $application)) { throw 'Unity returned success without producing CarAR.exe.' }
# A new local build supersedes any previously downloaded player.
$stamp = Join-Path $projectDir 'Build/Windows/carar-release.json'
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
    $vrLog = Join-Path $projectDir 'smoke-vr.log'
    $vrArgs = @('--vr-smoke', '-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1440', '-screen-height', '900', '-logFile', ('"' + $vrLog + '"'))
    $process = Start-Process -FilePath $application -ArgumentList $vrArgs -WorkingDirectory (Split-Path $application) -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $vrLog -Pattern '\[CarAR VR Smoke\] PASS')) {
        throw "VR regression failed. See $vrLog"
    }
}
Write-Host "Windows player ready: $application"

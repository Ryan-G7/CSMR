param([switch]$Autoplay, [switch]$Check, [switch]$Export, [string]$FfmpegPath = $env:CARAR_FFMPEG)
$ErrorActionPreference = 'Stop'
$presentationProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$presentationApplication = Join-Path $presentationProject 'Build/Presentation/CarAR.exe'
if (-not (Test-Path -LiteralPath $presentationApplication)) {
    throw 'Build the player with Tools/Build-Windows.ps1 -Presentation first.'
}
$presentationStart = New-Object System.Diagnostics.ProcessStartInfo
$presentationStart.FileName = $presentationApplication
$presentationStart.WorkingDirectory = Split-Path $presentationApplication
$presentationStart.Arguments = '--vr-desktop --vr-walkthrough -force-d3d11 -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 -logFile "' + (Join-Path $presentationProject 'presentation.log') + '"'
if ($Autoplay -or $Check) { $presentationStart.Arguments += ' --vr-presentation-autoplay' }
if ($Check) { $presentationStart.Arguments += ' --vr-walkthrough-check' }
if ($Export) {
    if (-not $FfmpegPath) {
        $encoder = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
        if ($encoder) { $FfmpegPath = $encoder.Source }
    }
    if (-not $FfmpegPath -or -not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
        throw 'Pass -FfmpegPath <ffmpeg.exe> or set CARAR_FFMPEG. FFmpeg must support libx264.'
    }
    $presentationStart.EnvironmentVariables['CARAR_FFMPEG'] = [IO.Path]::GetFullPath($FfmpegPath)
    $presentationStart.Arguments += ' --vr-walkthrough-export'
}
$presentationStart.UseShellExecute = $false
foreach ($presentationKey in @('XR_ENABLE_API_LAYERS', 'XR_API_LAYER_PATH', 'CARAR_HJXR_SDK')) { $presentationStart.EnvironmentVariables.Remove($presentationKey) }
$presentationStart.EnvironmentVariables['DISABLE_XR_APILAYER_HJMR_passthrough'] = '1'
$presentationProcess = [System.Diagnostics.Process]::Start($presentationStart)
if ($Check) {
    $presentationProcess.WaitForExit()
    if ($presentationProcess.ExitCode -ne 0) { throw "Walkthrough check failed ($($presentationProcess.ExitCode)). See presentation.log." }
    $presentationLog = Join-Path $presentationProject 'presentation.log'
    if (-not (Select-String -LiteralPath $presentationLog -Pattern '\[CarAR Walkthrough\] COMPLETE')) {
        throw 'Player exited before completing the walkthrough. See presentation.log.'
    }
    if ($Export -and -not (Select-String -LiteralPath $presentationLog -Pattern '\[CarAR Walkthrough Video\] SAVED')) {
        throw 'Player exited without a completed video. See presentation.log.'
    }
}

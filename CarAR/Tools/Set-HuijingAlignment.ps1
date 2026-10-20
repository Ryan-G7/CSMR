param(
    [ValidateSet(0, 1)] [int]$PoseDeferFrames = 0,
    [ValidateRange(-30, 60)] [double]$VideoPoseOffsetMs,
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\Build\Windows\CarAR.exe')
)
$ErrorActionPreference = 'Stop'
$target = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ApplicationPath))) 'hjxr-alignment.json'
$request = [ordered]@{
    poseDeferFrames = $PoseDeferFrames
    changeVideoTiming = $PSBoundParameters.ContainsKey('VideoPoseOffsetMs')
    videoPoseOffsetMs = $VideoPoseOffsetMs
}
$json = $request | ConvertTo-Json
$temporary = $target + '.tmp'
[IO.File]::WriteAllText($temporary, $json, (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Output "Session alignment request written: $target"
Write-Output $json

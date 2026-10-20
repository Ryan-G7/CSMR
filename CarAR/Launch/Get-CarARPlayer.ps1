param()
$ErrorActionPreference = 'Stop'
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildDir = Join-Path $projectDir 'Build'
$playerDir = Join-Path $buildDir 'Windows'
$application = Join-Path $playerDir 'CarAR.exe'
$release = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'player-release.json') -Raw | ConvertFrom-Json
$stampPath = Join-Path $playerDir 'carar-release.json'
if (Test-Path -LiteralPath $application) {
    # A local Unity build has no release stamp and takes priority over downloads.
    if (-not (Test-Path -LiteralPath $stampPath)) { return $application }
    $installed = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    if ($installed.sha256 -eq $release.sha256) { return $application }
}
if ($release.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Missing release checksum. Build locally with Tools/Build-Windows.ps1.' }
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$work = Join-Path $buildDir ('.download-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $zip = Join-Path $work 'CarAR-Windows-x64.zip'
    Write-Host "Downloading CarAR $($release.version) from GitHub Releases..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $previousProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest -Uri $release.url -OutFile $zip -UseBasicParsing -TimeoutSec 120
                break
            } catch {
                if ($attempt -eq 3) { throw }
                Write-Host 'Connection interrupted; retrying the release download...'
                Start-Sleep -Seconds 2
            }
        }
    } finally { $ProgressPreference = $previousProgress }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $release.sha256) {
        throw 'Player archive checksum mismatch. Installation aborted.'
    }
    $unpacked = Join-Path $work 'player'
    Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
    foreach ($required in @('CarAR.exe', 'UnityPlayer.dll', 'CarAR_Data')) {
        if (-not (Test-Path -LiteralPath (Join-Path $unpacked $required))) { throw "Incomplete player archive: $required" }
    }
    if (Test-Path -LiteralPath $playerDir) {
        # Preserve the old player and any local diagnostics during an update.
        Move-Item -LiteralPath $playerDir -Destination ($playerDir + '.previous-' + [Guid]::NewGuid().ToString('N'))
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'player-release.json') -Destination (Join-Path $unpacked 'carar-release.json')
    Move-Item -LiteralPath $unpacked -Destination $playerDir
} finally {
    # Only the exact temporary directory created above is removed.
    Remove-Item -LiteralPath $work -Recurse -Force
}
return $application

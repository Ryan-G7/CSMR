param([string]$UnityPath = $env:CARAR_UNITY_EDITOR, [string]$BlenderPath = $env:CARAR_BLENDER)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $UnityPath) { $UnityPath = Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe' }
if (-not $BlenderPath) { $BlenderPath = 'D:/Soft/Blender/blender.exe' }
foreach ($tool in @($UnityPath, $BlenderPath)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Required tool not found: $tool" }
}
$manifest = Get-Content -LiteralPath (Join-Path $project 'Assets/Environment/VR/carroom.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$source = $manifest.source_path
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Source .blend is required to rebake lighting: $source" }
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.source_sha256) {
    throw 'Source changed. Run Export-VRScene.ps1 first.'
}
$log = Join-Path $project 'vr-bake-geometry.log'
$unityArgs = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'CarAR.EditorTools.VRBakeGeometry.PrepareAndExport', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "VR atlas preparation failed. See $log" }
$env:OIIO_THREADS = '4'; $env:OPENIMAGEIO_THREADS = '4'
foreach ($script in @('bake_vr_cycles.py', 'bake_vr_interior_probe.py')) {
    & $BlenderPath --background --factory-startup --threads 8 $source --python-exit-code 1 --python (Join-Path $PSScriptRoot $script)
    if ($LASTEXITCODE -ne 0) { throw "Blender lighting failed: $script" }
}
Write-Host 'Source Cycles lighting is ready. Run Tools/Build-Windows.ps1 -Smoke to build and verify.'

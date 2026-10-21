param([string]$SourcePath = 'D:/BaiduNetdiskDownload/CarRoom-2.blend', [string]$BlenderPath = $env:CARAR_BLENDER, [switch]$Reference)
$ErrorActionPreference = 'Stop'
if (-not $BlenderPath) { $BlenderPath = 'D:/Soft/Blender/blender.exe' }
if (-not (Test-Path -LiteralPath $BlenderPath -PathType Leaf)) { throw 'Pass -BlenderPath <blender.exe>.' }
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scene = [IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $scene -PathType Leaf)) { throw "VR source not found: $scene" }
$directory = Join-Path $project 'Assets/Environment/VR'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$env:OIIO_THREADS = '4'; $env:OPENIMAGEIO_THREADS = '4'
$arguments = @('--background', '--factory-startup', '--threads', '8', $scene, '--python-exit-code', '1', '--python', (Join-Path $PSScriptRoot 'prepare_vr_environment.py'))
if ($Reference) { $arguments += @('--', '--reference') }
& $BlenderPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Blender scene preparation failed; source was not changed.' }
& $BlenderPath --background --factory-startup --threads 4 --python-exit-code 1 --python (Join-Path $PSScriptRoot 'prepare_vr_lighting.py')
if ($LASTEXITCODE -ne 0) { throw 'Source HDR preparation failed.' }
$output = Join-Path $directory 'CarRoom.glb'
$temporary = Join-Path $project 'tmp/CarRoom.glb'
& $BlenderPath --background --factory-startup --threads 8 (Join-Path $project 'tmp/vr-prepared.blend') --python-exit-code 1 --python (Join-Path $PSScriptRoot 'export_prepared_glb.py') -- $temporary
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporary)) { throw 'VR glTF export failed.' }
Move-Item -LiteralPath $temporary -Destination $output -Force
$manifest = Join-Path $directory 'carroom.json'
$report = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$report | Add-Member -MemberType NoteProperty -Name gltf_sha256 -Value ((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()) -Force
[IO.File]::WriteAllText($manifest, ($report | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
& $BlenderPath --background --factory-startup --threads 4 --python-exit-code 1 --python (Join-Path $PSScriptRoot 'export_vr_materials.py')
if ($LASTEXITCODE -ne 0) { throw 'Native VR material extraction failed.' }
Write-Host "VR source exported: $output"

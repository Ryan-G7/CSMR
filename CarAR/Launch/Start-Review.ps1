param([Parameter(Mandatory)][ValidateSet('VR', 'MR')][string]$Mode)
$ErrorActionPreference = 'Stop'
do {
    if ($Mode -eq 'VR') {
        Write-Host 'VR startup: 1. OpenXR VR  2. Varjo XR-4 VR  3. Desktop walkthrough'
    } else {
        Write-Host 'MR startup: 1. Huijing MR  2. Varjo XR-4 MR  3. Desktop preview'
    }
    $choice = Read-Host 'Select [1-3]'
} until ($choice -in @('1', '2', '3'))
switch ("${Mode}:$choice") {
    'VR:1' { & (Join-Path $PSScriptRoot 'Start-VR.ps1') }
    'VR:2' { & (Join-Path $PSScriptRoot 'Start-Varjo.ps1') }
    'VR:3' { & (Join-Path $PSScriptRoot 'Start-VR.ps1') -Desktop }
    'MR:1' { & (Join-Path $PSScriptRoot 'Start-HuijingMR.ps1') }
    'MR:2' { & (Join-Path $PSScriptRoot 'Start-Varjo.ps1') -MR }
    'MR:3' { & (Join-Path $PSScriptRoot 'Start-DesktopPreview.ps1') }
}

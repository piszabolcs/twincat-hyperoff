# TwinCAT HyperOFF (VbsAutoOff) v1.0.0 - compiler + portable packager
# Without SDK/NuGet, using the csc.exe of the .NET Framework 4.x built into Windows. No admin rights needed.
#   powershell -ExecutionPolicy Bypass -File .\build.ps1
# Output:
#   release\VbsAutoOff.exe                         (the raw build)
#   dist\TwinCAT_HyperOFF_v1.0.0_portable\         (portable folder)
#   dist\TwinCAT_HyperOFF_v1.0.0_portable.zip      (the same in a zip, this is what you hand out)
$ErrorActionPreference = 'Stop'
$version  = '1.0.0'
$root     = $PSScriptRoot
$src      = Join-Path $root 'src\VbsAutoOff'
$release  = Join-Path $root 'release'
$dist     = Join-Path $root 'dist'
$portName = "TwinCAT_HyperOFF_v$($version)_portable"
$portDir  = Join-Path $dist $portName
$zipPath  = Join-Path $dist "$portName.zip"
$icon     = Join-Path $root 'assets\app.ico'
$manifest = Join-Path $src 'app.manifest'

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "Built-in .NET compiler not found: $csc" }
$wpfDir = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
if (-not (Test-Path $icon)) { throw "Missing icon: $icon" }

foreach ($d in @($release, $dist)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null } }
$exe = Join-Path $release 'VbsAutoOff.exe'

$csFiles = Get-ChildItem -Path $src -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Select-Object -ExpandProperty FullName

Write-Host "Build starting..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$exe" `
    "/win32manifest:$manifest" `
    "/win32icon:$icon" `
    "/resource:$icon,VbsAutoOff.app.ico" `
    "/r:System.dll" `
    "/r:System.Core.dll" `
    "/r:System.Management.dll" `
    "/r:System.ServiceProcess.dll" `
    "/r:System.Xaml.dll" `
    "/r:$wpfDir\WindowsBase.dll" `
    "/r:$wpfDir\PresentationCore.dll" `
    "/r:$wpfDir\PresentationFramework.dll" `
    $csFiles
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "The build failed (ExitCode=$LASTEXITCODE)." }
$kb = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host "BUILD SUCCEEDED: $exe ($kb KB)" -ForegroundColor Green

# Assemble the portable folder
if (Test-Path $portDir) { Remove-Item $portDir -Recurse -Force }
New-Item -ItemType Directory -Path $portDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $portDir 'fallback_script') -Force | Out-Null
Copy-Item $exe (Join-Path $portDir 'TwinCAT_HyperOFF.exe') -Force
Copy-Item (Join-Path $root 'USAGE.txt') $portDir -Force
Copy-Item (Join-Path $root 'scrit\regedit.ps1') (Join-Path $portDir 'fallback_script\regedit.ps1') -Force

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $portDir '*') -DestinationPath $zipPath -Force
Write-Host "PORTABLE: $portDir" -ForegroundColor Green
Write-Host "ZIP:      $zipPath" -ForegroundColor Green

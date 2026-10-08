# Builds Pegline.exe into .\dist, plus a zip for sharing.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Configuration Release]
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\Pegline\Pegline.csproj'
$dist = Join-Path $root 'dist'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'The .NET SDK is needed to build. Install it with: winget install Microsoft.DotNet.SDK.10' }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& $dotnet build $project -c $Configuration -nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

New-Item -ItemType Directory -Force $dist | Out-Null
$bin = Join-Path $root "src\Pegline\bin\$Configuration\net48"
Copy-Item (Join-Path $bin 'Pegline.exe') $dist -Force

[xml]$csproj = Get-Content $project
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip = Join-Path $dist "Pegline-$version-windows.zip"
if (Test-Path $zip) { [System.IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $dist 'Pegline.exe') -DestinationPath $zip

$size = [math]::Round((Get-Item (Join-Path $dist 'Pegline.exe')).Length / 1KB)
Write-Host "Built dist\Pegline.exe ($size KB) and $(Split-Path $zip -Leaf)"

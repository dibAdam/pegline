# Installs Pegline for the current user: no admin rights, nothing outside
# your profile. Copies the app to %LOCALAPPDATA%\Programs\Pegline, adds it to
# the Start menu and starts it.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\install.ps1 [-OpenAtLogin]
param([switch]$OpenAtLogin)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$source = Join-Path $root 'dist\Pegline.exe'
if (-not (Test-Path $source)) { & (Join-Path $PSScriptRoot 'build.ps1') }

$target = Join-Path $env:LOCALAPPDATA 'Programs\Pegline'
$exe = Join-Path $target 'Pegline.exe'

# Quit a running copy so the file can be replaced.
if (Test-Path $exe) {
    & $exe --quit
    Start-Sleep -Seconds 1
}
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $source $exe -Force

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Pegline.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = 'Screenshots, pegged to a line at the top of your screen.'
$link.Save()

if ($OpenAtLogin) {
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Pegline' -Value "`"$exe`" --startup"
}

Start-Process $exe
Write-Host "Installed to $target and added to the Start menu."
Write-Host 'Tip: drag the Pegline icon out of the ^ overflow onto the taskbar to keep it in view.'

# Removes Pegline for the current user. Your screenshots are never deleted:
# captures kept in Pegline's inbox stay in %LOCALAPPDATA%\Pegline\Screenshots
# unless you pass -RemoveSettings, which removes its settings and log only.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1 [-RemoveSettings]
param([switch]$RemoveSettings)
$target = Join-Path $env:LOCALAPPDATA 'Programs\Pegline'
$exe = Join-Path $target 'Pegline.exe'

if (Test-Path $exe) {
    & $exe --quit
    Start-Sleep -Seconds 1
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Pegline' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name 'Pegline' -ErrorAction SilentlyContinue

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Pegline.lnk'
if (Test-Path $shortcut) { Remove-Item -LiteralPath $shortcut }
if (Test-Path $exe) { Remove-Item -LiteralPath $exe }
if ((Test-Path $target) -and -not (Get-ChildItem -LiteralPath $target)) { Remove-Item -LiteralPath $target }

if ($RemoveSettings) {
    Remove-Item -LiteralPath 'HKCU:\Software\Pegline' -Recurse -ErrorAction SilentlyContinue
    $data = Join-Path $env:LOCALAPPDATA 'Pegline'
    foreach ($log in 'pegline.log', 'pegline.log.old') {
        $path = Join-Path $data $log
        if (Test-Path $path) { Remove-Item -LiteralPath $path }
    }
}

$inbox = Join-Path $env:LOCALAPPDATA 'Pegline\Screenshots'
Write-Host 'Pegline is uninstalled.'
if (Test-Path $inbox) { Write-Host "Screenshots it was holding are still in $inbox" }

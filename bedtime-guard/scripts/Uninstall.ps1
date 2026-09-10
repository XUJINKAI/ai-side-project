[CmdletBinding()]
param([switch]$KeepData)
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Administrator)) {
    $extra = @(); if ($KeepData) { $extra += '-KeepData' }
    Restart-Elevated $PSCommandPath $extra; return
}
$root = Get-InstalledRoot
# Stop enforcement first; a failed restoration aborts before deleting recovery resources.
Invoke-Checked "$root/service/BedtimeGuard.Service.exe" @('--repair')
if (Test-Path (Join-Path $DataDir 'task-manager-lease.json')) { throw 'Policy restoration is pending. Sign in as the selected user and run uninstall again.' }
Invoke-Checked 'sc.exe' @('delete', $ServiceName)
Stop-ScheduledTask -TaskName $RecoveryTask -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $RecoveryTask -Confirm:$false -ErrorAction SilentlyContinue
$appPath = Join-Path $root 'app/BedtimeGuard.App.exe'
Get-Process -Name 'BedtimeGuard.App' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -eq $appPath) {
        Stop-Process -Id $_.Id -Force
        if (-not $_.WaitForExit(10000)) { throw 'User agent did not exit; keep installed files for retry.' }
    }
}
$shortcuts = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Bedtime Guard'
if (Test-Path $shortcuts) { Remove-Item $shortcuts -Recurse -Force }
if (-not $KeepData) { Remove-Item $DataDir -Recurse -Force }
Remove-Item $root -Recurse -Force
Write-Host 'Bedtime Guard uninstalled. Task Manager policy restored.'

[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Administrator)) { Restart-Elevated $PSCommandPath; return }
$root = Get-InstalledRoot
Invoke-Checked "$root/service/BedtimeGuard.Service.exe" @('--repair')
Write-Host 'Restrictions paused and policy restored. Run Resume.ps1 to re-enable the saved schedule.'

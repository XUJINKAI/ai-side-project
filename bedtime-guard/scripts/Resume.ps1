[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Administrator)) { Restart-Elevated $PSCommandPath; return }
$null = Get-InstalledRoot
$paused = Join-Path $DataDir 'paused'
if (Test-Path $paused) { Remove-Item $paused -Force }
Start-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
Write-Host 'Saved schedule resumed. If a frozen night is still active, it resumes too.'

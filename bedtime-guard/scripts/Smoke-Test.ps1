# Run only on an expendable Windows test host. Never enables locking or the real Task Manager policy.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Bundle)
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Administrator)) { throw 'Run this smoke test as administrator on a test host.' }
$destination = Join-Path $env:ProgramFiles 'BedtimeGuard-SmokeTest'
$installed = $false
try {
    & "$Bundle/scripts/Install.ps1" -InstallDir $destination
    $installed = $true
    $service = Get-Service BedtimeGuard
    if ($service.Status -ne 'Running') { throw 'Service did not start.' }
    # Exercise the real authenticated named pipe from the selected account.
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'BedtimeGuard.v1', [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::None, [Security.Principal.TokenImpersonationLevel]::Identification)
    try {
        $pipe.Connect(5000)
        $body = [Text.Encoding]::UTF8.GetBytes('{"Command":"status"}')
        $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
        $writer.Write([int]$body.Length); $writer.Write($body); $writer.Flush()
        $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
        $length = $reader.ReadInt32()
        if ($length -lt 1 -or $length -gt 32768) { throw 'Invalid response frame.' }
        $reply = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
        if (-not $reply.Ok -or $reply.Status.Phase -ne 0) { throw 'Expected disabled status from newly installed service.' }
    } finally { $pipe.Dispose() }
    & "$destination/scripts/Repair.ps1"
    if ((Get-Service BedtimeGuard).Status -ne 'Stopped') { throw 'Repair did not stop the service.' }
    & "$destination/scripts/Resume.ps1"
    if ((Get-Service BedtimeGuard).Status -ne 'Running') { throw 'Resume did not start the service.' }
    & "$destination/scripts/Uninstall.ps1"
    $installed = $false
    if (Test-Path $destination) { throw 'Install directory remains.' }
    if (Test-Path $DataDir) { throw 'State directory remains.' }
    if (Get-ScheduledTask -TaskName $RecoveryTask -ErrorAction SilentlyContinue) { throw 'Recovery task remains.' }
    Write-Host 'PASS install, real service IPC, administrator repair, resume and uninstall.'
} finally {
    if ($installed -and (Test-Path "$destination/scripts/Uninstall.ps1")) { & "$destination/scripts/Uninstall.ps1" }
}

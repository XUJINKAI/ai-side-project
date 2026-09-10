[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'BedtimeGuard'),
    [string]$UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
)
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Administrator)) {
    Restart-Elevated $PSCommandPath @('-InstallDir', "`"$InstallDir`"", '-UserSid', $UserSid)
    return
}
$identity = [Security.Principal.SecurityIdentifier]::new($UserSid)
# Validate a real account before creating a SYSTEM service. Capture SID before UAC elevation.
$null = $identity.Translate([Security.Principal.NTAccount])
$InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$programFiles = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\') + '\'
if (-not $InstallDir.StartsWith($programFiles, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDir must be a new subdirectory of Program Files, to protect the SYSTEM service binaries.'
}
Assert-NoReparseAncestor $InstallDir
Assert-NoReparseAncestor $DataDir
if ((Test-Path $InstallDir) -or (Test-Path $DataDir) -or (Get-Service $ServiceName -ErrorAction SilentlyContinue)) {
    throw 'An installation or saved state already exists. No files were overwritten. Use repair/resume, or uninstall first.'
}
$source = Split-Path $PSScriptRoot -Parent
foreach ($part in @('app', 'service')) {
    if (-not (Test-Path "$source/$part/BedtimeGuard.$part.exe")) { throw 'Run Install.cmd from a published bundle (build.ps1), not the source directory.' }
}
$serviceCreated = $false
$taskCreated = $false
$shortcuts = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Bedtime Guard'
if (Test-Path $shortcuts) { throw 'Start-menu folder already exists; refusing to overwrite it.' }
try {
    $null = New-Item $InstallDir -ItemType Directory
    Set-ProtectedDirectory $InstallDir $true
    $null = New-Item $DataDir -ItemType Directory
    Set-ProtectedDirectory $DataDir $false
    foreach ($item in @('app', 'service', 'scripts', 'README.md', 'docs')) {
        Copy-Item -LiteralPath (Join-Path $source $item) -Destination $InstallDir -Recurse
    }
    $app = Join-Path $InstallDir 'app/BedtimeGuard.App.exe'
    $service = Join-Path $InstallDir 'service/BedtimeGuard.Service.exe'
    Write-JsonAtomic (Join-Path $DataDir 'installation.json') @{ UserSid = $UserSid; AppPath = $app }
    Write-JsonAtomic (Join-Path $DataDir 'state.json') @{
        Version = 1
        Schedule = @{
            Enabled = $false; Commitment = '20:00:00'; JitterMinutes = 30; Reminder = '22:00:00'
            Bedtime = '23:00:00'; Release = '06:00:00'; Days = @(0,1,2,3,4,5,6)
            TimeZoneId = [TimeZoneInfo]::Local.Id; DisableTaskManager = $false
        }
        Draws = @{}; Frozen = $null; CompletedThrough = $null
    }
    $null = New-Service -Name $ServiceName -BinaryPathName "`"$service`"" -StartupType Automatic -DisplayName 'Bedtime Guard' -Description 'User-configured bedtime reminders and workstation locking. Administrator recovery is available from the Start menu.'
    $serviceCreated = $true
    Invoke-Checked $service @('--configure-recovery')
    # Independent periodic cleanup also handles crashes and a missed expiry while powered off.
    $action = New-ScheduledTaskAction -Execute $service -Argument '--recover-expired'
    $periodic = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $startup = New-ScheduledTaskTrigger -AtStartup
    $logon = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    if (Get-ScheduledTask -TaskName $RecoveryTask -ErrorAction SilentlyContinue) { throw 'Recovery task name already exists.' }
    $null = Register-ScheduledTask -TaskName $RecoveryTask -Action $action -Trigger @($periodic,$startup,$logon) -Principal $principal -Settings $settings
    $taskCreated = $true
    $null = New-Item $shortcuts -ItemType Directory
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $shortcuts 'Bedtime Guard.lnk'))
    $shortcut.TargetPath = $app; $shortcut.Save()
    foreach ($entry in @(@('Repair','恢复'), @('Resume','重新启用'), @('Uninstall','卸载'))) {
        $shortcut = $shell.CreateShortcut((Join-Path $shortcuts "Bedtime Guard — $($entry[1]).lnk"))
        $shortcut.TargetPath = "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe"
        $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$InstallDir/scripts/$($entry[0]).ps1`""
        $shortcut.Save()
    }
    Start-Service $ServiceName
    (Get-Service $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
    Write-Host 'Installed. Open Bedtime Guard from the tray or Start menu. The schedule starts DISABLED; review and enable it in Settings.'
} catch {
    $failure = $_
    if ($serviceCreated) {
        Stop-Service $ServiceName -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName | Out-Null
    }
    if ($taskCreated) { Unregister-ScheduledTask -TaskName $RecoveryTask -Confirm:$false -ErrorAction SilentlyContinue }
    if (Test-Path $shortcuts) { Remove-Item $shortcuts -Recurse -Force }
    # No schedule was enabled during installation, hence no policy lease should exist.
    if (Test-Path (Join-Path $DataDir 'task-manager-lease.json')) {
        Write-Warning 'Recovery journal found. Keeping installation and data for administrator repair.'
    } else {
        if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
        if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force }
    }
    throw $failure
}

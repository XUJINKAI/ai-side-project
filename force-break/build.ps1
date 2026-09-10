# Developer build entry point only. No scripts are shipped to end users.
[CmdletBinding()]
param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/ForceBreak.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ([Environment]::OSVersion.Platform -eq 'Win32NT') {
        dotnet run --project tests/ForceBreak.Windows.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Windows policy tests failed.' }
        dotnet run --project tests/ForceBreak.App.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Overlay UI tests failed.' }
    }
    $output = Join-Path $PSScriptRoot "out/ForceBreak-$Runtime"
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    dotnet publish src/ForceBreak.App/ForceBreak.App.csproj -c Release -r $Runtime --self-contained false -p:PublishSingleFile=true -m:1 -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $files = @(Get-ChildItem $output -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'ForceBreak.exe') { throw 'Expected exactly one framework-dependent EXE.' }
    Write-Host "Built $($files[0].FullName) ($($files[0].Length) bytes). Requires .NET 10 Desktop Runtime."
} finally { Pop-Location }

[CmdletBinding()]
param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/BedtimeGuard.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ([Environment]::OSVersion.Platform -eq 'Win32NT') {
        dotnet run --project tests/BedtimeGuard.Windows.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Windows policy tests failed.' }
    }
    $bundle = Join-Path $PSScriptRoot "out/BedtimeGuard-$Runtime"
    if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
    foreach ($part in @('App', 'Service')) {
        $destination = Join-Path $bundle $part.ToLowerInvariant()
        dotnet publish "src/BedtimeGuard.$part/BedtimeGuard.$part.csproj" -c Release -r $Runtime --self-contained true -m:1 -o $destination
        if ($LASTEXITCODE -ne 0) { throw "Publish $part failed." }
    }
    Copy-Item scripts -Destination $bundle -Recurse
    Copy-Item README.md, docs -Destination $bundle -Recurse
    Copy-Item scripts/Install.cmd -Destination $bundle
    $zip = "$bundle.zip"
    Compress-Archive -Path "$bundle/*" -DestinationPath $zip -Force
    Write-Host "Built $zip"
} finally { Pop-Location }

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force out | Out-Null
$targets = @(@('windows','amd64','lansend-windows-amd64.exe'), @('windows','arm64','lansend-windows-arm64.exe'), @('linux','amd64','lansend-linux-amd64'), @('linux','arm64','lansend-linux-arm64'))
foreach ($target in $targets) { $env:CGO_ENABLED='0'; $env:GOOS=$target[0]; $env:GOARCH=$target[1]; go build -trimpath -ldflags '-s -w' -o "out/$($target[2])" ./cmd/lansend }

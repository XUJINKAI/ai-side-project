VERSION ?= 0.1.0

.PHONY: test build release
build: clean
	CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o out/lansend ./cmd/lansend
test:
	gofmt -w $$(find . -name '*.go' -not -path './out/*')
	go vet ./...
	go test ./...
release: test
	mkdir -p out
	CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -ldflags "-s -w" -o out/lansend-linux-amd64 ./cmd/lansend
	CGO_ENABLED=0 GOOS=linux GOARCH=arm64 go build -trimpath -ldflags "-s -w" -o out/lansend-linux-arm64 ./cmd/lansend
	CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -trimpath -ldflags "-s -w" -o out/lansend-windows-amd64.exe ./cmd/lansend
	CGO_ENABLED=0 GOOS=windows GOARCH=arm64 go build -trimpath -ldflags "-s -w" -o out/lansend-windows-arm64.exe ./cmd/lansend
clean:
	rm -rf out

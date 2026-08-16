package web

import (
	"embed"
	"io/fs"
)

//go:embed index.html login.html style.css app.js login.js
var assets embed.FS

func Assets() (fs.FS, error) { return fs.Sub(assets, ".") }

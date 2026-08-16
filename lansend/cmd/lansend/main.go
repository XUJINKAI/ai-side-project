package main

import (
	"context"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"github.com/lansend/lansend/internal/config"
	"github.com/lansend/lansend/internal/files"
	"github.com/lansend/lansend/internal/server"
	"github.com/lansend/lansend/internal/texthub"
	"github.com/lansend/lansend/web"
	"github.com/skip2/go-qrcode"
)

func main() {
	c, done, e := config.Parse(os.Args[1:], os.Stdout)
	if e != nil {
		fmt.Fprintln(os.Stderr, "参数错误:", e)
		os.Exit(2)
	}
	if done {
		if len(os.Args) > 1 && os.Args[1] == "--version" {
			fmt.Println("lansend", config.Version)
		}
		return
	}
	st, e := files.New(c.DownloadDir, c.UploadDir, c.MaxFile, c.MaxStorage)
	if e != nil {
		fmt.Fprintln(os.Stderr, e)
		os.Exit(3)
	}
	hub := texthub.New(c.ClipboardLimit)
	if b, e := os.ReadFile(filepath.Join(c.UploadDir, "upload-text")); e == nil {
		hub.Put(string(b))
	}
	ui, _ := web.Assets()
	ln, e := net.Listen("tcp", fmt.Sprintf("%s:%d", c.Host, c.Port))
	if e != nil {
		fmt.Fprintln(os.Stderr, "无法绑定监听地址:", e)
		os.Exit(4)
	}
	srv := &http.Server{Handler: server.New(c, st, hub, ui).Handler(), ReadHeaderTimeout: 10 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16 << 10}
	fmt.Println("LanSend 已启动")
	fmt.Println("下载目录:", c.DownloadDir)
	if !c.NoUpload {
		fmt.Println("上传目录（按需创建）:", c.UploadDir)
	}
	for _, addr := range addresses(c.Port) {
		entry := addr
		label := "访问地址:"
		if c.Token == "" {
			fmt.Println(label, entry)
		} else {
			label = "入口地址:"
			entry = fmt.Sprintf("%s/enter?token=%s", addr, c.Token)
			fmt.Println(label, entry)
		}
		if c.QRCode {
			if code, err := qrcode.New(entry, qrcode.Medium); err == nil {
				fmt.Print(terminalQRCode(code))
			}
		}
	}
	go srv.Serve(ln)
	ch := make(chan os.Signal, 1)
	signal.Notify(ch, os.Interrupt, syscall.SIGTERM)
	<-ch
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	srv.Shutdown(ctx)
}

func terminalQRCode(code *qrcode.QRCode) string {
	code.DisableBorder = true
	bitmap := code.Bitmap()
	var out strings.Builder
	for y := 0; y < len(bitmap); y += 2 {
		for x := range bitmap[y] {
			top := bitmap[y][x]
			bottom := y+1 < len(bitmap) && bitmap[y+1][x]
			switch {
			case top && bottom:
				out.WriteRune('█')
			case top:
				out.WriteRune('▀')
			case bottom:
				out.WriteRune('▄')
			default:
				out.WriteRune(' ')
			}
		}
		out.WriteByte('\n')
	}
	return out.String()
}
func addresses(port int) []string {
	out := []string{}
	ifs, _ := net.Interfaces()
	for _, i := range ifs {
		if i.Flags&net.FlagUp == 0 || i.Flags&net.FlagLoopback != 0 {
			continue
		}
		as, _ := i.Addrs()
		for _, a := range as {
			var ip net.IP
			switch x := a.(type) {
			case *net.IPNet:
				ip = x.IP
			case *net.IPAddr:
				ip = x.IP
			}
			if ip != nil && ip.To4() != nil {
				out = append(out, fmt.Sprintf("http://%s:%d", ip, port))
			}
		}
	}
	if len(out) == 0 {
		out = []string{fmt.Sprintf("http://127.0.0.1:%d", port)}
	}
	return out
}

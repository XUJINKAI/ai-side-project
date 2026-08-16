package config

import (
	"errors"
	"flag"
	"fmt"
	"io"
	"strconv"
	"strings"
)

const Version = "0.1.0"

type Config struct {
	DownloadDir, UploadDir, Host, Token string
	Port                                int
	NoUpload, NoDownload, QRCode        bool
	MaxFile, MaxStorage, ClipboardLimit int64
}

func Defaults() Config {
	return Config{DownloadDir: ".", UploadDir: "./lansend-data", Host: "0.0.0.0", Port: 8080, MaxFile: 2 << 30, MaxStorage: 20 << 30, ClipboardLimit: 1 << 20}
}
func Parse(args []string, out io.Writer) (Config, bool, error) {
	c := Defaults()
	fs := flag.NewFlagSet("lansend", flag.ContinueOnError)
	fs.SetOutput(out)
	fs.Usage = func() {
		fmt.Fprint(out, "用法: lansend [选项] [DIR]\n\nDIR 是供下载浏览的目录，默认当前目录；它不会被创建或修改。\n\n选项:\n  --upload-dir DIR       上传文件和 upload-text 的保存目录（默认 ./lansend-data）\n  --no-upload            关闭文件和文本上传\n  --no-download          关闭目录浏览与下载\n  --host ADDRESS         绑定地址（默认 0.0.0.0）\n  --port PORT            绑定端口（默认 8080）\n  --token TOKEN          启用访问令牌验证\n  --qrcode, --qr         在终端显示入口地址二维码\n  --max-file SIZE        单文件上限（默认 2G）\n  --max-storage SIZE     上传目录总上限（默认 20G）\n  --clipboard-limit SIZE 上传文本上限（默认 1M）\n  -h, --help             显示此帮助\n  --version              显示版本\n")
	}
	fs.StringVar(&c.UploadDir, "upload-dir", c.UploadDir, "upload directory")
	fs.StringVar(&c.Host, "host", c.Host, "bind host")
	fs.IntVar(&c.Port, "port", c.Port, "bind port")
	fs.StringVar(&c.Token, "token", c.Token, "access token")
	fs.BoolVar(&c.NoUpload, "no-upload", false, "disable upload")
	fs.BoolVar(&c.NoDownload, "no-download", false, "disable download")
	fs.BoolVar(&c.QRCode, "qrcode", false, "show QR code")
	fs.BoolVar(&c.QRCode, "qr", false, "show QR code")
	var maxFile, maxStorage, clip string
	fs.StringVar(&maxFile, "max-file", "2G", "")
	fs.StringVar(&maxStorage, "max-storage", "20G", "")
	fs.StringVar(&clip, "clipboard-limit", "1M", "")
	help := fs.Bool("help", false, "")
	fs.BoolVar(help, "h", false, "")
	version := fs.Bool("version", false, "")
	if err := fs.Parse(args); err != nil {
		return c, false, err
	}
	if *help {
		fs.Usage()
		return c, true, nil
	}
	if *version {
		return c, true, nil
	}
	if fs.NArg() > 1 {
		return c, false, errors.New("最多只能指定一个 DIR")
	}
	if fs.NArg() == 1 {
		c.DownloadDir = fs.Arg(0)
	}
	var err error
	if c.MaxFile, err = ParseSize(maxFile); err != nil {
		return c, false, fmt.Errorf("--max-file: %w", err)
	}
	if c.MaxStorage, err = ParseSize(maxStorage); err != nil {
		return c, false, fmt.Errorf("--max-storage: %w", err)
	}
	if c.ClipboardLimit, err = ParseSize(clip); err != nil {
		return c, false, fmt.Errorf("--clipboard-limit: %w", err)
	}
	if c.Port < 1 || c.Port > 65535 {
		return c, false, errors.New("--port 必须介于 1 和 65535")
	}
	return c, false, nil
}
func ParseSize(s string) (int64, error) {
	s = strings.ToUpper(strings.TrimSpace(s))
	mult := int64(1)
	if len(s) > 0 {
		switch s[len(s)-1] {
		case 'K':
			mult = 1 << 10
		case 'M':
			mult = 1 << 20
		case 'G':
			mult = 1 << 30
		case 'T':
			mult = 1 << 40
		default:
			n, e := strconv.ParseInt(s, 10, 64)
			if e != nil || n < 0 {
				return 0, errors.New("无效大小")
			}
			return n, nil
		}
		s = s[:len(s)-1]
	}
	n, e := strconv.ParseInt(s, 10, 64)
	if e != nil || n < 0 || n > (1<<63-1)/mult {
		return 0, errors.New("无效大小")
	}
	return n * mult, nil
}

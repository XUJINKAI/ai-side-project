# LanSend 设计文档

> 项目名：LanSend（局域箱）
> 命令名：`lansend`
> 目标版本：v0.1.0

## 目标

LanSend 是一个仅依赖 Go 标准库的局域网临时文件站。服务端只需运行一个二进制文件；其他设备通过浏览器或 `curl` 下载既有文件、上传文件及交换短文本。不依赖云服务、数据库、客户端安装或后台守护进程。

v0.1.0 不包含公网暴露、用户账户、文件同步、端到端加密或原生剪贴板同步。

## 目录模型

```text
lansend [OPTIONS] [DIR]
```

- `DIR` 是下载根目录，默认当前目录 `.`。它必须已经存在，LanSend 只读访问其中的普通文件和目录，网页可逐级浏览子目录并下载文件。
- 默认不创建 `lansend-data/`，也不会向 `DIR` 写入任何文件。
- 上传和共享文本使用独立的上传目录，默认 `./lansend-data/`，可用 `--upload-dir DIR` 指定。
- 上传目录按需创建：第一次上传写入 `UPLOAD_DIR/<文件名>`，第一次保存文本写入 `UPLOAD_DIR/share`。
- 符号链接、非普通文件和目录以外的特殊条目不展示、不下载，避免借目录浏览读取范围外的文件。
- 上传同名文件保留两个副本，例如 `report (1).pdf`。

这使得 `lansend --no-upload DIR` 可作为纯下载文件服务器；`lansend --no-download` 则可作为只上传/文本中转站。

## 命令行

```text
lansend [选项] [DIR]

  --upload-dir DIR       上传文件和 share 文本的保存目录（默认 ./lansend-data）
  --no-upload            关闭上传及共享文本写入
  --no-download          关闭目录浏览与下载
  --host ADDRESS         绑定地址（默认 0.0.0.0）
  --port PORT            绑定端口（默认 8080）
  --token TOKEN          启用访问令牌验证
  --max-file SIZE        单文件上限（默认 2G）
  --max-storage SIZE     上传目录总上限（默认 20G）
  --clipboard-limit SIZE 共享文本上限（默认 1M）
  -h, --help             显示帮助
  --version              显示版本
```

`-h` 和 `--help` 打印帮助后以状态码 0 退出。`--host` 与 `--port` 组成实际监听地址；默认监听所有 IPv4 接口。启动日志枚举已启用网卡的实际 IPv4 地址（例如 `10.64.0.10`），而不将监听通配地址或 IPv6 未指定地址作为给用户的入口地址。

## 认证与网络边界

默认不启用令牌。指定 `--token KEY` 后，终端显示：

```text
http://10.64.0.10:8080/enter?token=...
```

入口验证成功后设置 `HttpOnly`、`SameSite=Strict` Cookie 并重定向至不含令牌的首页。直接打开页面时，浏览器会弹出令牌输入框并通过 `Authorization: Bearer <token>` 调用 API；`curl` 使用相同的 Bearer 形式。

LanSend 不修改防火墙、不做 NAT 穿透，也不应直接映射到公网。跨网访问应使用 WireGuard、Tailscale 或 SSH 隧道等受控通道。

## HTTP API

所有 API 位于 `/api/v1/`。未认证请求返回 `401`；成功 JSON 响应没有 `error`，失败响应为：

```json
{"error":{"code":"not_found","message":"资源不存在"}}
```

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/status` | 返回上传、下载功能开关 |
| GET | `/files?path=<id>` | 列出下载根目录或子目录；省略 `path` 列出根目录 |
| GET/HEAD | `/files/{id}` | 下载指定普通文件，支持 Range |
| POST | `/upload` | `multipart/form-data`，字段名为 `file`；保存到上传目录 |
| GET | `/text` | 读取当前共享文本 |
| PUT | `/text` | 覆盖共享文本，并同步写入上传目录的 `share` |
| DELETE | `/text` | 清空内存文本并删除 `share` |

文件和目录的 `id` 为 URL 安全编码的相对路径。解码后仍须进行清理和根目录边界校验，不能将 URL 中的任意路径直接拼为系统路径。

`--no-download` 时下载接口返回 `403`；`--no-upload` 时上传以及文本的 PUT/DELETE 返回 `403`，但仍可读取已有 `share` 内容。

## Web 页面

页面使用嵌入二进制的原生 HTML、CSS、JavaScript，无 CDN 和 Node.js 构建步骤。页面分为三个面板：

1. **下载文件**：展示当前位置和文件/目录列表，目录可进入、可返回上级，文件以附件方式下载。
2. **上传文件**：选择或拖放多个文件；每个文件单独请求，并显示进度和结果。
3. **共享文本**：显示当前文本、字符数和更新时间，支持保存、清空和 Clipboard API 的复制回退。

服务根据 `/status` 隐藏被 `--no-upload` 或 `--no-download` 禁用的面板或操作。

## 安全和资源限制

- 指定 `--token` 时 API 要求令牌或会话 Cookie；未指定时不认证。
- 设置 `X-Content-Type-Options: nosniff` 和仅允许本站资源的 CSP。
- 上传文件名不允许空名称、控制字符、路径分隔符或路径穿越。
- 上传流写入临时文件，成功后原子重命名；不将整文件载入内存。
- `--max-file`、`--max-storage` 和 `--clipboard-limit` 约束资源消耗。
- 下载使用 `http.ServeContent`，支持 GET、HEAD 和范围请求，并以 `attachment` 响应。

## 源码与构建

```text
lansend/
├── cmd/lansend/       # 主程序
├── internal/           # auth、config、files、server、texthub
├── web/                # 被 go:embed 的页面资源
├── out/                # 本地构建产物（忽略）
├── README.md
└── Makefile
```

使用 Go 1.26+。`make test` 执行格式化、`go vet` 和测试；`make release` 生成 Linux/Windows 的 amd64、arm64 静态二进制到 `out/`。

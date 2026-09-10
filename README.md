# ai-side-project

一个由 AI 协作构建的多项目实验仓库。这里不限定语言、领域或应用类型；每个子项目都应保持独立、可构建、可测试，并遵循根目录的 [协作规范](AGENTS.md)。

## 当前项目

### 工具

- #### [bedtime-guard](bedtime-guard/) (C# / Windows)

随机承诺时间的睡眠提醒与强制锁屏工具，包含托盘界面、Windows 服务、可选任务管理器限制和管理员恢复。

- #### lansend (go)

局域网传输工具，通过浏览器上传和下载，支持文本预览和上传。

### 终端游戏

- #### npuzzle (C语言)

3×3 至 12×12 的滑块拼图游戏，支持键盘、鼠标和可切换操作模式。

手写版本2012到2022，见 legacy\npuzzle-legacy-2012-2022.c

- #### hanoi (C语言)

3 至 12 个圆盘的汉诺塔游戏，支持数字键与方向键选柱。

纯 AI 仿照 npuzzle 完成。

### 库

- #### cts (C语言)

C Type System, 为 C 的结构体定义类型，支持 JSON 序列化/反序列化。

22 年未完成的想法，由 AI 完成。

## 下载最新构建

[Latest builds](https://github.com/XUJINKAI/ai-side-project/releases/tag/latest-build) 在发布配置合并到主分支、首次构建成功后自动建立。每个项目独立更新，未改动的项目保留上次产物。

| 文件 | 内容 |
| --- | --- |
| `lansend-win-x64.exe` | LanSend Windows x64 |
| `lansend-linux-amd64` | LanSend Linux x86-64，下载后按需添加执行权限 |
| `bedtime-guard-win-x64.exe` | Bedtime Guard Windows x64，需要 .NET 10 Desktop Runtime |
| `lansend-build.json` | 两个平台 LanSend 的构建来源与校验信息 |
| `bedtime-guard-build.json` | Bedtime Guard 的构建来源与校验信息 |

[发布流程与 JSON 字段说明](.github/release/README.md)。Release 附件为原始文件，不打包；PR 的 Actions artifact 只供预览。

## 约定

- 每个项目拥有自己的 README、构建脚本、测试和忽略规则。
- 可再生构建产物统一放入项目自己的 `out/`（或等价目录），不提交到版本控制。
- 优先提供 Make 与 CMake 两种构建方式；具体要求以各项目 README 为准。
- 系统安装命令会在覆盖既有程序前请求确认。

## 添加新项目

在仓库根目录创建独立目录，提供最少的源码、构建说明、测试和 README；然后在本文件的“当前项目”表格中登记它。

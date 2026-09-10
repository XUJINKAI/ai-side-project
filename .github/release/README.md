# 主分支滚动发布

默认分支 `master` 的项目构建成功后，共同更新 `latest-build` Release。PR 仅测试与保存 Actions artifact，不发布 Release；手动运行项目 workflow 可以强制重新构建，只有选择默认分支才允许发布。

## 固定附件

- `lansend-win-x64.exe`
- `lansend-linux-amd64`
- `force-break-win-x64.exe`
- `lansend-build.json`
- `force-break-build.json`

三个程序均直接上传，不生成 ZIP、TAR 或独立校验文件。Actions 内部跨 job 传递使用 artifact，最终 Release 附件是原始文件。GitHub 自带的 Source code ZIP/TAR 入口不属于本流程上传的产物。

Linux 下载后可能需要 `chmod +x lansend-linux-amd64`。Force Break 下载文件采用统一名称，安装后的内部文件名仍为 `ForceBreak.exe`，无需用户重命名；需要 .NET 10 Desktop Runtime x64。LanSend 使用 CGO_ENABLED=0 构建 Windows/Linux x86-64 两个平台。

## 构建与更新规则

每个项目的 workflow 只监听该项目目录、自身 workflow 和共享发布逻辑。主分支构建前，比较项目目录、项目 workflow、共享 publish workflow 与发布脚本目录的 Git 对象哈希和已发布 JSON 中的输入指纹；输入一致且附件齐全则跳过构建。项目目录的文档修改也属于输入变化。工具链远端补丁更新不会自行触发构建，需要手动重建。

第一次合并这些 workflow 时，两项目分别构建并补齐五个附件。以后修改 bedtime 不编译 lansend；共享发布脚本改变则重新验证两个项目。

发布作业共用 `latest-build-publish` 并发组，使用 `queue: max` 排队，避免相互取消待发布作业。发布前重新取得默认分支最新提交；只有本项目构建输入仍匹配时才允许上传。其他项目的无关提交不会导致本项目产物被丢弃。一个项目构建失败不影响另一个项目发布，失败项目保留上一次成功产物。

仅更新本项目的固定文件；JSON 最后上传。替换前下载本项目的旧文件备份，上传后下载核对字节；可捕获的失败会尝试恢复旧文件。GitHub 不提供多附件原子事务，上传期间可能短暂存在新旧文件混合，强制终止 runner 可能阻止回滚。遇到上传失败或文件与 JSON 校验不符，在默认分支手动重跑对应 workflow 修复。

## JSON 构建信息

每个项目一份，LanSend 的 JSON 同时列出两个平台文件。字段包含：

- `schema_version`、`project`、`repository`
- `source_commit`：实际检出的源码提交；PR artifact 可能是合并测试提交
- `source_ref`、`build_inputs_sha256`
- `built_at`：UTC 时间；`workflow_run_url`
- `toolchain`：实际 SDK/Go 版本
- `runtime_requirement`
- `files[]`：固定文件名、操作系统、架构、字节数及 SHA-256

不同项目的源码提交可以不同；以各自 JSON 为准。滚动标签初始化后不移动，GitHub 自动生成的 Source code 附件对应初始化提交，不能代替各二进制的源码版本记录。

## 权限与首次使用

使用 GitHub 自动提供的 GITHUB_TOKEN，无需手动填写 PAT。测试/构建只需要 contents: read，发布 job 声明 contents: write。首次发布自动创建 Release，无需预先建立标签或 Release。

配置合并进默认分支后才会发布。若仓库或组织策略禁止 Actions 写入，发布步骤会明确失败，需允许该工作流写仓库内容。若启用了不可变 Release，`latest-build` 不能滚动覆盖，需调整该发布策略；脚本会拒绝修改不可变 Release。

## 验证

```sh
python -m unittest discover -s .github/release -p 'test_*.py' -v
```

测试覆盖独立项目更新、错误校验拒绝、缺失产物重建、非主分支拒绝、旧构建拒绝，以及覆盖失败回滚。这些测试模拟 API；真实 Release 上传需在合并默认分支后的首次发布确认。

发布成功后清理已从项目目录表移除的构建：根据本仓库的构建 JSON 确认归属，仅删除该退出项目声明的附件和构建 JSON。当前项目、其他项目和无关附件不会被删除。

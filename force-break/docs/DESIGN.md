# Force Break 设计

## 权威与状态

服务以 LocalSystem 运行，持有配置、随机样本、冻结快照。App 以原用户身份运行；服务绝不把 SYSTEM UI 放进交互桌面。WTSQueryUserToken + CreateProcessAsUser 只启动固定安装路径的客户端，并检查用户 SID，不接受客户端传来的命令、路径或会话身份。

Named Pipe 仅允许 SYSTEM 与选定 SID，拒绝 Network SID。收到请求后根据客户端令牌再次核验身份。每个连接最多一个请求，帧长上限 32 KiB，超时 5 秒，最多四个并发连接。业务命令包括 `status`、`save`、`activity`、`rest`、`rest-tomorrow`。手动休息时长接受 1—120 整数分钟；直到明天的请求由服务根据计划时区计算截止时间；不接收自选路径或会话身份。原始随机样本和具体 CommitAt 不进入响应。

服务在单个锁内复制当前状态、计算，配置与冻结快照先持久化再发布。未承诺的休息累计进度每 15 秒保存，正常停止时补存；崩溃最多损失约 15 秒进度。服务 tick 每秒一次，界面也每秒请求状态。无全系统进程高频扫描；监督器每 5 秒只检查同名单 EXE 进程及真实路径。

`PlannerState` 保存：

- `Schedule`：未来计划，包含星期、时区、时间、任务管理器选项。
- `Draws`：按晚上日期保存的随机整数。编辑日程只映射已有样本，不重新抽取；不对客户端公开。
- `Frozen`：已进入承诺期的当晚具体时间点与策略选项。
- `Break`：未承诺工作累计秒数，以及可选的本轮固定提醒/锁屏/解除时间与策略快照。
- `CompletedThrough`：已经完成的夜晚，不因时钟回拨而重新执行。

每次保存前先按旧规则 tick，确保“保存恰好落在承诺截止时刻”不会绕过冻结。Frozen 存在时配置保存只影响以后，即使取消星期、关闭 Enabled、修改时区或策略选项都不会重写今晚。

承诺、首次提醒与限制开始必须按当天时间递增；整个随机窗口严格早于首次提醒。次日解除必须早于下一晚最早承诺时刻。夏令时不存在的本地时间推进到首个有效分钟；重复时间按开始较早、解除较晚处理。主动改系统时间不在防绕过范围内。

## 策略恢复事务

生产策略路径固定为 `HKEY_USERS/<SID>/Software/Microsoft/Windows/CurrentVersion/Policies/System/DisableTaskMgr`。

1. 确认未被域/MDM 管理，用户 hive 已加载，原值不存在或为 DWORD 0。
2. 原子写入恢复记录：SID、原值是否存在、原值、到期时间、心跳。
3. 设置 DWORD 1，并刷新。
4. 服务每 30 秒刷新心跳，不刷新固定的到期时间。
5. 到期、停服务、管理员恢复/卸载、独立恢复任务触发时尝试还原。
6. 只有当前值仍是本工具写入的 DWORD 1 才改回原值，避免覆盖其他工具的后续修改；操作成功后才删恢复记录。

恢复记录先于策略写入，所以崩溃发生在任意一步都可以重试。服务和独立恢复进程通过排他文件句柄串行化策略操作；锁忙则后续重试，不删除日志。注册表 hive 未加载时保留记录。损坏的记录不猜测原值，不直接删值，交给管理员排查。

独立恢复任务在安装时建立，开机、登录及每分钟运行；服务不需要在每晚创建任务。管理员恢复先写入 `paused`，阻止服务再次施加限制，然后停止服务、恢复策略。卸载只有在日志成功清除后才移除恢复任务和程序。

## 故障与保活

客户端检查服务的即时状态，按已冻结的行为独立执行多屏遮罩与可选 LockWorkStation。会话锁定/解锁事件通过 WTS 通知接收，失败请求限制重试频率。锁屏是异步系统请求，不把返回 true 误当成已锁定。

服务失联超过 5 秒时让遮罩显示关闭按钮并停止使用陈旧状态锁屏，保留正在编辑的笔记。App 崩溃时最多 3 次/5 分钟启动，服务崩溃最多两次 SCM 自动重启；无驱动、注入、进程隐藏或阻止管理员停止。数据损坏时拒绝重置规则并尝试释放策略，独立恢复任务处理遗留租约。

## 安装边界

Program Files 直属的安装目录只允许 SYSTEM/Administrators 写入，普通 Users 读/执行；不接受经过其他可写中间目录的安装路径。ProgramData 状态仅 SYSTEM/Administrators 可访问。安装目标和祖先不得为重解析点。记录原始用户 SID 后再 UAC 提权，不将另一个管理员账号误当成受约束用户。

首次安装不自动启用日程；已有安装或数据必须经图形确认后覆盖。覆盖停止并刷新旧服务，备份状态后更新程序和服务登记，保留原用户、计划、承诺和暂停状态。失败保留数据和恢复记录供重试。卸载只结束真实路径匹配的 ForceBreak.exe，不触碰其他进程。

## Win32 参考

- [LockWorkStation 与异步结果](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-lockworkstation)
- [服务与用户会话分离](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)
- [WTSQueryUserToken](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsqueryusertoken)
- [DisableTaskMgr 用户策略](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-ctrlaltdel#disabletaskmgr)

## 定时休息

服务使用 Stopwatch 测量相邻刷新间隔，WTS SessionInfoEx 判断目标 SID 的活动会话是否解锁。超过 5 秒的刷新间隔视为睡眠/停顿，不算工作时间。承诺前以累计工作秒数判断阈值；跨过 d 分钟阈值时立即保存绝对时间快照。之后不因锁屏、配置修改或重新登录推迟本轮。c ≤ d，保证看到提醒时已经承诺；整数分钟配置要求 a−d≥11。Frozen 存在时直接返回本轮状态，不累计下一轮。状态格式版本为 1，读取未知版本时拒绝启动。

保存请求先按旧配置计算冻结，再计算新配置。夜间限制清零自动周期休息计时，手动休息快照保留至其截止时间；其他情况下休息锁屏优先于夜间提醒，重叠提醒按最近锁屏选择。临时策略的租约覆盖两个已承诺计划所需的最晚解除时间；其中一个结束不会提前解除另一个的策略。

## 单 EXE 与图形维护

App 是唯一发布入口，引用服务库。普通启动进入 WPF 管理界面，`--service` 进入 ServiceBase，`--recover-expired` 执行策略恢复。工作间隔统一为 WorkMinutes。Framework-dependent PublishSingleFile，不打包运行时；要求匹配架构的 .NET 10 Desktop Runtime。无脚本调用、外部命令或用户命令行步骤。

安装按钮先记录原用户 SID，复制单 EXE 到临时目录，再通过 ShellExecute runas 请求 UAC；进程参数采用 ArgumentList。提权副本显示维护进度和错误，直接调用 SCM Win32、任务计划 COM、快捷方式 COM 与注册表 API。卸载副本位于安装目录外，可以结束路径完全匹配的托盘并删除安装 EXE。卸载登记 `--uninstall-ui` 同样打开图形确认。紧急暂停/强制卸载只接受 README 中的完整长参数，无图形入口或短别名。普通卸载先停止并刷新服务状态，再计算当前承诺；拒绝时恢复原来运行的服务，不留下暂停标记。

- [WTS 会话解锁状态](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w)
- [.NET 单文件发布](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 输入活动、显示与模块边界

- `BehaviorOptions` 为独立不可变配置，休息和早睡快照均保存它。`Status.EffectiveBehavior` 来自当前快照，而不是未来配置。任务管理器开关与其他行为一起在承诺时固定。
- `InputActivityMonitor` 位于 Windows 层，独立线程安装两个低级 Hook 并运行原生消息循环。回调仅更新单调时钟时间戳并调用下一 Hook，不阻断输入、不传递内容；忽略注入事件。每分钟重新安装 Hook，降低 Windows 静默移除超时 Hook 后长期漏检的可能。线程定时心跳用于判断消息泵是否停顿，Dispose 在原线程清理 Hook 和计时器。
- App 每秒发送 Available/IdleSeconds/OverlayVisible。服务用 GetNamedPipeClientSessionId 获取真实会话 ID，结合 WTS 目标 SID、活动/解锁状态和五秒检测租约。新报告到达前先结算上一报告对应的经过时间，不能把先前空闲段补算成工作。服务拥有工作时间计数，客户端不能指定计数增量。
- `ActivityLease` 在 Core 内独立验证报告的新鲜度和空闲阈值，使用单调时钟；空闲只影响承诺前累计，承诺后继续绝对时间安排。输入检测不等同于注意力检测，无操作阅读会判为空闲。
- `RestOverlay` 仅接收截止时间与标题，不依赖计划状态机或锁屏。它管理全部显示器窗口，适应显示布局变化，显示向上取整的剩余分钟及共享笔记；有效限制期阻止关闭，到期仅转为可关闭状态。它不是安全桌面，不承诺拦截系统快捷键。
- `WorkstationLock` 独立处理锁屏调用、会话锁定状态及限频。提醒弹窗不再兼任遮罩。
- 手动休息复用持久化 `FrozenBreak`：立即进入限制、工作累计清零、到期才开始下一轮；禁止替换有效休息承诺。早睡限制优先，策略租约继续覆盖所有相关承诺的最晚解除时刻。

Hook 线程与超时处理依据 Microsoft 文档：[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)、[LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)。

## 遮罩生命周期与笔记

`OverlaySession` 独立管理 Closed / Manual / Restricted / Completed 四种显示状态。服务解除或倒计时到期只切换到 Completed，用户明确关闭才销毁窗口；新有效限制可以重新进入 Restricted。手动打开不能把 Restricted 降级。多屏布局重建只重建窗口，笔记模型不变。

`OverlayNotes` 保存用户文本，UTF-8 临时文件写入并刷新后原子替换。App 定时保存，关闭前再次保存；保存失败不关闭，读取失败不允许编辑以防覆盖原文件。笔记存于当前用户 LocalApplicationData，不上传服务，卸载不删除。

OverlayVisible 心跳在所有行为配置下生效，使遮罩中的键鼠操作不会被算作工作。UI 失联五秒后显示状态租约失效，仍由会话与活动条件决定是否计时。

跨夜手动休息在 FrozenBreak 中显式标记，允许超过自动休息的三小时上限，但最多五十小时覆盖下一日及夏令时变化；自动周期没有获得这个例外。早睡和手动休息同时限制时，显示最晚解除时间，遮罩和锁屏取仍然有效的两个快照的合并要求；一个到期后继续另一个的行为。

命令行输出由 `CommandOutput` 处理：保留原始重定向句柄后附加父控制台，真实控制台使用 WriteConsoleW，管道匹配 GetConsoleOutputCP，文件或无控制台管道使用 UTF-8。不修改父控制台代码页。参考 [WriteConsole](https://learn.microsoft.com/en-us/windows/console/writeconsole)。

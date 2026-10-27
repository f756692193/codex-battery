# Windows 使用与构建

当前版本 **1.0.0**。C# / WinForms，运行环境为 Windows 10 / 11 自带的 .NET Framework 4.8。

## 普通使用

1. 解压便携发行包，双击 `CodexQuotaManager.exe`，无需安装或管理员权限。
2. 首次运行自动寻找本机 Codex 并查询当前登录账号；查询需要联网。
3. 未找到 Codex 时，在设置中选择 `codex.exe`。不要选择本工具自己的 EXE。
4. 未登录或登录过期时，先在 Codex 中登录，再点刷新。本工具不要求密码、验证码或 API Key。

旧版用户先从托盘退出旧版，再打开新版。重复启动会唤起正在运行的版本，不会替换旧进程。

## 电池与悬停

- 小浮窗默认 64×24 逻辑像素，绿色填充表示剩余额度，百分比保留最多一位小数。
- 拖动电池改变位置；双击电池打开完整详情；右键可刷新、设置、隐藏、退出。
- 悬停面板逐行显示实际周期的进度条、比例、重置倒计时与具体时间。重置卡独立一行。
- “重置卡”下方显示最早到期日期、时间及倒计时，沿用北京时间 / 系统时区设置。小于 3 天用黄色，小于 24 小时及已到期用红色。
- 只有服务端返回完整有效的可用卡详情时才计算最早到期；只返回数量、详情被截断或字段无效时显示“到期时间待确认”。0 张显示暂无可用卡，明确不失效的卡显示无到期限制。
- 卡到期后等待服务端确认，不在本地扣减数量，也不自动换用下一张卡的到期时间。
- 鼠标可从电池移动到面板继续点击，离开后收起。
- 点击刷新时面板保持展开，显示“刷新中…”并原位更新结果；查询过程中不重复发起刷新。
- 点击设置无需先打开主窗口。主窗口隐藏时设置单独显示，并在任务栏提供入口。
- 满足条件的 Tibo 消息显示在卡数和底部操作之间。消息仅为近 3 天的重置卡候选帖；点击打开原帖。
- 来源有效且有未打开的消息时，电池端子变红；旧来源不亮新消息提醒。
- 未知额度显示 `—`；到期不会自动补满。

## 托盘与退出

左键托盘打开完整详情；窗口的 X 收回托盘，继续运行。右键托盘选择“退出管家”才会彻底结束。退出会清理本工具创建的查询进程，不结束已有的 Codex。

开机启动默认关闭，可在设置中开启。移动 EXE 后如需开机启动，重新保存设置更新路径。

## 从源码构建

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

产物：`windows\dist\CodexQuotaManager.exe`。使用 Windows 自带的 .NET Framework C# 编译器，不依赖 Visual Studio、.NET SDK、Node.js 或 Python。

## 测试和发行包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\tests\run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1
```

测试使用合成账号和 RSS 数据，不要求 Codex 登录，包含 46 项额度 / 进程、35 项消息、16 项界面操作检查。界面检查会短暂显示本工具窗口，设置窗口自动取消，不写入偏好设置。打包脚本生成 `artifacts\CodexBattery-windows-1.0.0.zip`，包含 EXE、使用说明、验证说明和 SHA256。

手动验证真实账号可运行：

```powershell
$folder = Join-Path (Get-Location) 'windows\tests\results-manual'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Start-Process .\windows\dist\CodexQuotaManager.exe -ArgumentList @('--smoke-ui', ('"' + $folder + '"')) -Wait
Get-Content (Join-Path $folder 'ui-smoke.json')
```

该模式会显示本工具自己的窗口、只读查询实际账号和公开来源，并测试关闭、重新打开及退出。输出可能含有本机路径与使用情况，保留在被 Git 忽略的测试目录中。

## 本地数据

继续沿用 `%LOCALAPPDATA%\CodexQuotaManager`，包括 `settings.json`、额度缓存、通知去重记录、`tibo-news.json`。仓库改名不会改动已有配置路径。

完整便携包说明保留在 [README.txt](README.txt)。隐私、限制和排错见根目录 `docs/`。

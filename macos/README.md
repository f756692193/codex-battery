# macOS 使用与构建

**0.1.0 实验版。当前开发设备是 Windows，本目录源码尚未在 Mac 编译、运行或验证登录。不能按已可用的正式发行版介绍。**

采用 Swift + AppKit，最低目标 macOS 13。没有第三方 Swift 依赖。核心数据模型与进程、消息代码在 `BatteryCore`，界面代码在 `CodexBattery`。

## 构建条件

- 一台 macOS 13+ 的 Mac。
- Xcode Command Line Tools，提供 Swift 5.9+ 与 Apple SDK。工具链版本过旧时更新 Xcode。
- 本机可运行并已登录的 Codex，且当前账号支持 ChatGPT 订阅额度查询。
- 查询额度和消息需要联网。

## 构建、测试、运行

在仓库根目录执行：

```bash
xcode-select --install           # 已安装则跳过
swift --version
swift test --package-path macos
bash macos/build.sh
open "macos/dist/Codex Battery.app"
```

默认只构建当前 Mac 架构。生成 Apple Silicon / Intel 通用版本：

```bash
ARCHS=universal bash macos/build.sh
```

产物：

```text
macos/dist/Codex Battery.app
macos/dist/CodexBattery-macos-native.zip     # 或 universal.zip
macos/dist/SHA256.txt
```

可将 `.app` 复制到 `/Applications` 或 `~/Applications` 后双击。构建阶段需要开发工具，运行 `.app` 不需要用户安装 Swift。

脚本仅做本地 ad-hoc 签名，没有 Developer ID 签名和 Apple 公证。跨设备分发可能被 Gatekeeper 拦截；发行准备见 [发布说明](../docs/RELEASING.md)，不要求用户关闭系统安全设置。

## 界面与操作

- 默认常驻菜单栏，同时显示绿色电池浮窗，没有 Dock 图标。
- 拖动电池后保存位置。悬停展开详情，鼠标移入详情后可以点击，离开后收起。
- 菜单栏左键打开 / 收起概要，右键打开菜单。电池右键也可打开菜单。
- 悬停显示实际周期、进度条、剩余比例、重置时间与卡数。
- Tibo 消息仅显示近 72 小时内重置卡相关候选帖子。点击摘要交给默认浏览器打开原帖，成功交接后标记为已打开。
- 无合格消息时隐藏消息区。菜单中的“Tibo 重置卡消息”会固定打开消息状态，可点“收起”。
- 从菜单选择“退出 Codex Battery”后停止所有定时查询和本工具创建的 helper，不结束用户原有 Codex。

## Codex 路径与登录

自动查找候选包括：

```text
/Applications/Codex.app/Contents/Resources/codex
~/Applications/Codex.app/Contents/Resources/codex
/opt/homebrew/bin/codex
/usr/local/bin/codex
~/.local/bin/codex
~/.cargo/bin/codex
PATH 中的 codex
```

这些是候选路径，桌面应用未来可能调整内部布局。找不到时在设置中填写实际可执行文件路径。GUI 程序的 PATH 与终端可能不同，不会启动交互 shell 来加载你的 shell 配置。

额度通过所选 Codex 自己的 App Server 读取，不直接打开 auth.json 或钥匙串。若该程序未识别到当前账号，先在相同 Codex 环境中登录，再刷新。不要把 Windows 的配置或令牌复制到 Mac。

## 设置与本地文件

在菜单“设置…”中配置 Codex 路径、可选 HTTPS RSS 源、浮窗开关、消息检查开关和系统时区。

本地目录为：

```text
~/Library/Application Support/CodexBattery/
  settings.json
  tibo-news.json
```

不把额度旧缓存作为实时数据使用。macOS 首版没有开机启动开关，可由用户自行在系统登录项中添加 `.app`。卸载时退出程序并删除 `.app`；需要清理设置时再删除上述本地目录。

## 尚需 Mac 完成的验证

1. `swift test`、原生架构构建与 universal 构建。
2. Apple Silicon / Intel 上启动、菜单栏、拖动、悬停、链接与设置。
3. 使用真实本机登录查询额度、重置卡；验证未登录、网络失败、账号切换。
4. 睡眠唤醒、周期到期、未知状态、多屏和 Retina 缩放。
5. 正常退出、重复启动和查询超时后检查自建 helper 清理。
6. 新消息、已打开版本、过期消息、过旧来源和抓取失败状态。

源码测试覆盖额度解析、跨桶选择、卡数、消息过滤、URL 校验、版本状态、合成 RSS/索引和 fake helper RPC。未执行的检查不能视为已经通过。

# 构建与发布

## Windows

仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1
```

脚本先运行额度、消息和界面回归测试，再编译并生成 `artifacts\CodexBattery-windows-1.0.0.zip`。界面测试短暂显示本工具窗口并自动关闭，不读真实账号或保存设置。保留现有 EXE 名 `CodexQuotaManager.exe` 以兼容配置、开机启动和旧版用户。

版本来自根目录 `VERSION`；本机只保留固定的 `windows/dist` 和一份当前Windows便携包，不创建版本目录。新包成功创建后脚本清理旧Windows ZIP，历史发行包可另存Releases。版本选择规则见 [VERSIONING.md](VERSIONING.md)。

正式发布前完成真实账号与界面验证，检查退出后 helper 已结束。不要把真实验证输出或账号截图放入源码提交。维护者可自行增加 Windows Authenticode 签名，当前未签名。

## macOS

在 Mac 的仓库根目录执行：

```bash
swift test --package-path macos
ARCHS=universal bash macos/build.sh
file "macos/dist/Codex Battery.app/Contents/MacOS/CodexBattery"
codesign --verify --strict "macos/dist/Codex Battery.app"
```

`build.sh` 生成 `.app`、ZIP 与校验值，做 ad-hoc 签名。**当前尚未实际执行 Mac 构建，也未生成可承诺使用的 Mac 发行包。**

面向其他 Mac 公开分发时，由维护者使用自己的 Apple Developer ID 签名、启用需要的 hardened runtime 配置、使用 `notarytool` 提交公证，并 staple 后重新打包、生成 SHA256。签名身份与凭据不写入仓库。先完成真实登录、菜单栏、浮窗、退出和双架构验证。

## CI

`.github/workflows/build.yml` 配置 Windows 构建 / 测试 / 便携包和 macOS XCTest / universal `.app` 构建。触发方式包括 push、pull request 和手动运行。

CI 使用合成数据，不读取真实账号，不要求 Codex 安装或登录。CI 构建通过只能证明对应 runner 的测试和编译，不证明真实账号、跨设备 UI、签名公证或发行质量。

当前只提供配置文件，未连接远端仓库、未触发 CI、未上传任何制品。

## 发布记录

1. 更新平台版本：Windows 的根目录 `VERSION` 与对外文档（程序集版本和包名自动同步）；Mac 的 `Info.plist`、RPC clientInfo 与文档。
2. 更新 `CHANGELOG.md` 与 `docs/VALIDATION.md`，区分实际通过和未执行的检查。
3. 验证压缩包内 EXE / `.app` 与构建产物一致，附 SHA256。
4. 在仓库 Releases 或内部制品库上传产物，并明确平台、架构、实验状态和签名状态。

此文档只描述发布步骤，不代表已经授权自动发布、提交或推送。

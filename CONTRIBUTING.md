# 开发与提交

## 修改范围

- 界面实现分属 `windows/` 和 `macos/`；修改数据规则时检查两端一致性。
- 协议字段、有效期、卡数来源、消息过滤变化应同步更新 `docs/DATA-SOURCES.md`。
- 真实账号数据只用于本地验证。测试使用 `example.invalid` 和明确标注的合成消息。
- 不加入密码、令牌、账号缓存、真实验证结果或下载来的历史公开消息全集。

## 验证

Windows：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\tests\run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

Mac：

```bash
swift test --package-path macos
bash macos/build.sh
```

构建和模型测试不能替代真实账号与交互验证。Mac 尚未执行的验证清单在 `macos/README.md`，执行后将环境和结果写入 `docs/VALIDATION.md`。

## Git

本次整理不自动创建提交或推送。进入项目目录先检查：

```bash
git status --short
git diff --check
git add .
git diff --cached --stat
git diff --cached --check
git commit -m "Initial import of Codex Battery"
```

仓库尚未设置远端。创建自己的空仓库后再绑定真实地址：

```bash
git remote add origin <你的真实Git仓库地址>
git push -u origin HEAD
```

尖括号为说明占位，不能原样执行。默认初始分支为 `codex/initial`，可按仓库约定调整。若远端已有内容，先查看并协调历史，不强制推送。

当前未指定开源许可证；公开分发策略由仓库维护者决定。其他项目的参考和本项目实现范围见 `NOTICE.md`。

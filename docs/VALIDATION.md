# 验证记录

版本：Windows **1.0.0**。日期：2026-10-27。验证平台：Windows。

## Windows

- 当前功能包含48项额度/进程、35项消息和16项界面检查，共99项；首次1.0.0验证为97项，后续CI编码回归补充2项。
- 检查涵盖额度周期与跨桶选择、缺失和过期状态、卡数权威来源、最早到期、详情不完整、时区、无到期限制、倒计时与卡到期待确认。
- 界面检查覆盖刷新保留面板、查询中和结果更新、隐藏主窗口从浮窗/托盘打开设置、可见主窗口打开设置。
- 开发阶段已使用真实账号验证额度、卡数和最早到期时间；公开记录仅保留通过结果，不记录账号身份或实际数值。
- 合成预览和真实账号悬停图已检查。绿色电池、到期行、消息区和底部操作无重叠；小电池仍为64×24逻辑像素。
- 主窗口关闭后留托盘、重开与退出清理自建helper检查通过。
- 本次统一发布版本与构建目录，未修改额度、重置卡和消息的业务规则。
- 1.0.0重新执行97项检查并编译成功，文件版本为1.0.0.0，产品版本为1.0.0；README与ZIP包名同步。
- 1.0.0真实账号查询、关闭到托盘、重开和helper退出检查通过；公开记录不包含实际额度、卡数或到期日期。

真实输出和截图不纳入公开仓库，完成本地验证后应删除。界面检查通过本程序的实际链接事件和Windows窗口可见性检查执行，未模拟系统鼠标。不代表已完成全部设备、DPI、通知配置和签名测试。

## macOS 0.1.0 实验版

提供Swift/AppKit源码、XCTest、Info.plist和`.app`构建脚本，之前只在Windows做过Swift语法树、Bash语法、plist/YAML和文档链接静态检查。

尚未成功完成Swift类型检查/链接、XCTest、Mac原生或universal构建，也未验证实际登录、菜单栏/浮窗操作、退出清理、多屏/Retina、签名和公证。当前没有经过Mac验证的正式发行包。

2026-10-27：GitHub首次macOS任务在编译`BatteryCore`时报告`no such module 'FoundationXML'`，测试尚未开始。Apple平台的`XMLParser`由`Foundation`提供，独立`FoundationXML`模块是Swift Core Libraries在其他平台上的组织方式。`News.swift`已改为使用`#if canImport(FoundationXML)`条件导入，保留`import Foundation`，无需增加外部依赖。

平台用法依据：[Apple XMLParser文档](https://developer.apple.com/documentation/foundation/xmlparser)、[Swift官方模块拆分与条件导入说明](https://forums.swift.org/t/new-swift-corelibs-foundation-module-foundationxml/27544)。本机没有Swift工具链与macOS SDK，本次仅核对平台用法、检查源码改动及差异格式；编译、测试和打包仍需推送后在macOS CI中重新确认。

## Windows CI 编码问题与隔离构建复核

2026-10-27：GitHub首次Windows任务在`CoreTests`模拟RPC首次通信处超时，尚未执行后续消息、界面检查和打包。此前本机通过不代表该次云端检查通过。

问题可在本机切换父进程控制台输入为带BOM的UTF-8后复现：.NET Framework的`Process.StandardInput`沿用父进程控制台编码，测试用模拟服务原先通过`Console.ReadLine`按自身默认编码读取，首条JSON因UTF-8前导标记而解析失败。该模拟通信不访问真实Codex、账号或网络。

修复仅涉及测试：模拟服务改为显式UTF-8输入流（识别并消费BOM）和不带BOM的UTF-8输出流；增加带BOM控制台输入下的RPC往返和辅助进程清理2项回归检查。应用业务源码、版本号与查询超时未调整。

复核方式为从`git archive HEAD`导出已提交源码，仅覆盖本次测试修复，在两个全新目录中分别运行。开始时不存在`dist`、`obj`、生成图标、测试EXE或发行包。使用Windows PowerShell 5.1、`-NoProfile`、`en-US`区域设置和仅含Windows系统目录的PATH，PATH中没有Codex。

- 默认控制台输入编码与UTF-8输入编码两种环境均通过48项额度/进程、35项消息、16项界面检查，共99项。
- 两种环境均成功重新编译EXE并生成Windows 1.0.0便携包。
- ZIP解压后包含EXE、README、验证说明和SHA256四个文件；EXE产品版本与VERSION一致，校验值匹配。

这是本机的源码隔离与环境差异复核，不是全新Windows虚拟机验证。本次修复尚未提交或推送，GitHub托管Windows环境仍需在推送后重新运行，以新运行结果确认云端状态。macOS模块导入问题见上节，仍待云端重跑验证。

## Git 与 CI

CI配置包含Windows构建/检查/打包与macOS检查/构建。远端已有首次运行记录；本次本机复核没有触发远端重跑，也没有提交或推送。

构建产物、生成版本源、运行配置、凭据与真实账号输出均被Git忽略。

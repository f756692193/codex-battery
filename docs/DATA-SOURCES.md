# 数据来源与显示规则

## Codex 账号额度

协议依据：[OpenAI Docs：Codex App Server](https://learn.chatgpt.com/docs/app-server)，重点为初始化、账号读取与 Rate limits 小节。

查询顺序：

1. 启动本机 `codex app-server --listen stdio://`。
2. 发送 `initialize`，收到响应后发送 `initialized`。
3. `account/read`，参数 `refreshToken: false`。
4. `account/rateLimits/read`。
5. 再次 `account/read`，检查账号是否发生变化。
6. 解析需要的字段并关闭本次查询 helper。

数据字段与界面映射：

| 字段 | 用途 |
| --- | --- |
| `account.type` | 确认是否为支持订阅额度的 ChatGPT 账号 |
| `account.email` | 同次查询前后账号检查；Windows 完整详情做脱敏显示 |
| `account.planType` | 套餐标签 |
| `rateLimitsByLimitId` | 多额度桶，优先于兼容字段 |
| `rateLimits` | 没有非空多桶视图时的兼容回退 |
| `usedPercent` | 剩余 = 100 - 已用比例，限制到 0～100 |
| `windowDurationMins` | 周期真实长度，不从 primary / secondary 名称推断 |
| `resetsAt` | 下次重置 Unix 秒时间戳 |
| `rateLimitResetCredits.availableCount` | 可用重置卡的权威总数 |
| `rateLimitResetCredits.credits[].expiresAt` | 每张卡独立的到期 Unix 秒时间戳，Windows 1.0.0 起显示 |

卡数缺失、无效或额度查询失败时显示“待更新”，不会把缺失当作 0。卡详情可能被服务端截断，不能用数组长度替代总数。

Windows 仅在可用卡详情完整时计算最早到期：按照独立卡 ID 去重，校验 `status: available` 与有效 `expiresAt` 字段，完整卡数应与权威 `availableCount` 一致。详情缺失、截断、重复或日期无效时显示到期时间待确认。

已用本机 Codex 的 `app-server generate-json-schema` 核对 `RateLimitResetCredit.expiresAt`：明确的 `null` 表示该卡不失效，缺失字段不能当作不失效。完整详情全为不失效卡时显示无到期限制；混合时只在有期限的卡中取最早日期。

日期沿用面板时区并附倒计时。已到期但服务端仍返回可用时标记已到期、待确认，不自行扣减数量或跳到下一张卡。macOS 0.1 暂未实现到期行。

默认从 Codex 桶选有效窗口中剩余最少的一个作为小电池数值；无 Codex 桶时使用首个实际桶。Windows 还支持用户明确选择其他周期。悬停按照桶和时长展示，不把无关桶的比例相加。

成功结果的有效期默认不超过 150 秒；Windows 自定义较长轮询间隔时相应延长。窗口重置时间到期后该窗口立即显示未知，只有查询到新数据才能恢复，不在本地补满。

当前协议基于 Codex 的 App Server，将来 CLI、登录机制或字段变化可能需要更新适配。

## Tibo 重置卡消息

只接受目标账号 `@thsottiaux` 的 HTTPS 原帖地址，主机仅允许 X / Twitter 已列明域名，路径必须是该账号的 `/status/数字ID`。拒绝其他作者、伪造域名、非 HTTPS、带账号信息的链接。

默认第三方 RSS：

```text
https://rsshub.stsecurity.moe/twitter/user/thsottiaux
```

默认源不可用或帖子过旧时尝试第三方公开索引：

```text
https://raw.githubusercontent.com/AllenXiao95/codex-reset-signal/monitor-state/status.json
```

两者均非 X 官方直连 API，可能延迟、漏帖或停更。本项目对其可用性没有保证；设置里可改用目标账号的 HTTPS RSS。

过滤与状态：

- 根据原帖发布时间过滤最近 72 小时；允许最多 5 分钟的时钟偏差，明显未来时间不显示。
- 只保留 banked reset、reset card、reset credit、重置卡/券等卡相关表述。普通额度重置、模型发布、一般赠品不显示。
- 摘要优先截取卡相关句子，限制为单行；只渲染纯文本，不执行 HTML 或 JavaScript。
- 最新观察到的目标账号原帖超过 48 小时时来源标记过旧。RSS 构建时间、索引更新时间不能代替原帖时间。
- 来源不健康或过旧时，不亮新消息提醒；仍在 72 小时内的已知消息可保留并标记待更新。
- 点击原帖成功交给默认浏览器后记录该版本为“已打开”；这不表示用户已核实或卡已到账。
- URL、发布时间、规范化文本共同决定消息版本。原帖文本变化后可重新提醒。
- 超过 72 小时的消息从显示和消息缓存移除。帖子从有限时间线消失不证明它被撤回。

消息展示、已打开状态与账号额度独立。公告不会增加本地卡数或触发卡兑换。

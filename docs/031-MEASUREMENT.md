# 0.3.1 测量方法与结果

本文件区分“较早候选的初步 30 分钟”“精确最终的 Windows 75 秒 + 手机仪器化 70 秒”和“未测试项”。**较早候选的 30 分钟结果不是最终候选结果**；精确最终的手机采集已在重新连接 USB 后取回并分析，但正常使用 30 分钟 FPS 仍未测试。未执行项标注 NOT TESTED，不使用推测数值，也不把辅助夹具当成硬件验收。

## 术语与预算

| 术语 | 定义 |
|---|---|
| 实际刷新率 | 参考设备实测 60.000004 Hz，不是近似写成 60 Hz 后计算。 |
| FPS | 唯一 presented 帧数除以观测时长。 |
| 1% Low | `1000 / mean(最慢 1% 的正向 inter-present 间隔 ms)`。 |
| Missed refresh opportunities | `错过的 60Hz 机会 / (已观测间隔数 + 错过机会数)`。与 dropped buffer、jank 分开报告。 |
| Dropped buffer | FrameTimeline 记录的 dropped buffer，单独统计。 |
| Jank | FrameTimeline 中 jank_type 非 None 的帧比例，包含 Buffer Stuffing 等不同原因；不把所有 jank 都称为错过截止期，也不与 missed refresh 混同。 |
| Physical update | 每个 WebView visual-ready 标记之后的第一个 app 帧，按 display token 与 SurfaceFlinger 实际结束时间连接；同一显示帧内多次更新只计一次。 |
| Age | 从 Windows 原生网络样本到保守显示帧，使用校准时钟；上界包含实测时钟不确定度。 |

## 方法

### 帧与显示

Perfetto FrameTimeline 的 app slice 结束点代表 GPU/提交完成，不等于物理呈现；必须把 app 的 `display_frame_token` 与 SurfaceFlinger 的实际结束连接，才能统计 presented 帧。参见 [Perfetto FrameTimeline](https://perfetto.dev/docs/data-sources/frametimeline)。

WebView 的 `postVisualStateCallback` 只表示“可进入下一次绘制”，**不是物理呈现**；因此本文件不把回调次数当作 FPS。参见 [Android WebView](https://developer.android.com/reference/android/webkit/WebView)。帧阶段与时间戳语义参考 [FrameMetrics](https://developer.android.com/reference/android/view/FrameMetrics)。

本次最终短窗口及较早 30 分钟窗口均包含 visual-fence 测量开销。最终版本关闭该记录器后的正常使用 FPS 尚未单独测量；后续如测，应使用 Perfetto 并关闭该记录器。任何 `requestAnimationFrame` 计数都不作为实际 FPS。

### 原生采样与字段时间戳

- CPU：250ms，`GetSystemTimes`。
- 网络：100ms，`GetIfEntry2`。
- RAM：500ms，`GlobalMemoryStatusEx`；已用值与百分比来自同一次调用，界面只显示如 `RAM 5.2 GiB · 67%`。
- 采样合并节拍门从样本 **START** 计时，避免 >20ms 的原生读取吞掉下一个 100ms 节拍。
- CPU/RAM 的新样本率约 4/2 Hz，与网络 10Hz 不同；保留各字段自身时间戳，不把旧字段重新标成新样本。
- 发送计数使用“进程生命周期成功加密 STATE 写入”；逐连接计数器会在重连时重置。早期对逐连接计数器做首尾相减得到的 8.836 已判定 INVALID，最终分析以 `null` 表示，不得引用为实际发送率。

### 24 小时观察器的边界

`Measure-CompanionStability.ps1` 只认证其采样的原生/接收/DOM/SQLite/资源证据，并与物理 FPS 分开。短预检只证明脚本可运行，不是 24 小时验收；本轮 24 小时由用户取消。

## 初步 30 分钟（较早候选，非最终候选）

| 指标 | 实际值 |
|---|---|
| 观测时长 | 1800064 ms |
| 实际刷新率 | 60.000004 Hz |
| 平均 FPS | 59.3996 |
| 1% Low FPS | 30.2772 |
| Missed refresh opportunities | 0.9684% |
| Dropped buffer | 0% |
| 非 None jank | 41.09%（包含 Buffer Stuffing 等原因，不等同所有帧错过截止期） |
| inter-present 间隔 | p50 16.672 ms / p95 16.895 ms / p99 17.071 ms / max 316.799 ms |
| 手机接收状态 | 9.6559 Hz |
| 手机 DOM 更新 | 9.6523 Hz |
| 手机 physical network 更新 | 9.4623 Hz |
| 端到端 physical age（不确定度上界） | p95 **307.289 ms（超过 300 ms 预算 → FAIL）**；p99 480.858 ms（低于 500 ms） |
| 电池温度 | 33–34°C（`ACTION_BATTERY_CHANGED` 电池温度，**不是 SoC 温度**） |
| Windows 原生网络 / CPU / RAM | 9.7701 / 3.9646 / 2.0001 Hz |
| 平均 CPU | 0.45654% |
| 峰值未完成帧 | 3 |
| 实际发送率 | `null`（逐连接计数器重置；8.836 为无效首尾相减） |

窗口内观察到真实重连；连接计数因计数器重置不可用，不能据此计算整窗发送率。该表只描述较早候选，不能外推为精确最终候选。

该 30 分钟候选的 EXE SHA-256 为 `5C353AF04A36B6DF00AC22CADFA6C74ACADD9BD21AC99CD46462D707CEA20758`，APK 为 `B989646BC37B01CAA3C6662E9C17C4C249A72A37278F160CADA42F865A3F7700`；与下方交付产物不同。首 5 分钟平均 59.4504 FPS，末 5 分钟 59.3871 FPS，电池温度 33→34°C；充电条件下的电池温度不能代表 SoC 温度。

## 精确最终 Windows 75 秒

| 指标 | 实际值 |
|---|---|
| 观测时长 / 检查次数 | 74882 ms / 703 |
| 网络原生样本 | 9.88763 Hz |
| CPU 新样本 | 3.97286 Hz |
| RAM 新样本 | 1.99976 Hz |
| 成功加密 STATE 写入 | 737 次；进程生命周期计数器 9.84215 Hz |
| 平均每状态线上字节 | 563.75 B |
| 窗口内新连接 / 断连检查异常 | 0 / 0 |
| 峰值未完成帧 | 3 |
| EXE SHA-256 | `768CF093CB34BCB395B902ABF22E153F3DBA30447E548D916A4C39E5DA137508` |
| 部署 APK SHA-256 | `B39AA4F68F64919EE08694A210F326F4C0E104B484A11524D972B9C735AEFA77` |

严格 10Hz 未完全达到：网络为 9.88763 Hz。手机端结果来自独立的 70.057 秒仪器化采集，见下一节，不以 Windows 窗口推断。

## 精确最终手机 70.057 秒（仪器化采集）

用户重新连接 USB 后，父级拉取并分析了已完成的采集；**没有启动新的长测试**。Windows 窗口 74.882 秒，手机采集 70.057 秒，Perfetto trace 约 73 秒。该采集包含 visual-fence 插桩开销，**不等同正常使用 FPS**。

| 指标 | 值 |
|---|---|
| Windows 原生 network / CPU / RAM | 9.887628 / 3.972857 / 1.999760 Hz |
| 发送 / 接收 / DOM | 9.842152 / 9.820156 / 9.822316 Hz |
| physical NEW network updates | 9.531574 Hz（667 次；频率按首末呈现事件之间的 666 个间隔计算） |
| 实际 physical FPS | 59.295125 |
| 1% Low | 29.935753 |
| Missed refresh opportunities | 1.142074% |
| Dropped buffer | 0% |
| FrameMetrics callbacks / drops | 4151 / 0 |
| 实际刷新率 | 60.000004 Hz |
| 网络 physical age（点位）P95 / P99 | 274.690625 / 392.009584 ms |
| 网络 physical age（含时钟不确定度）P95 / P99 | 279.690625 / 397.009584 ms |
| CPU 首次显示 | 277 个唯一样本，3.956623 Hz；上界 P95 / P99 334.41626 / 453.76880 ms |
| RAM 首次显示 | 140 个唯一样本，1.997903 Hz；上界 P95 / P99 334.21094 / 450.56152 ms |
| 电池温度 | 35°C，14 个样本，USB 连接；`ACTION_BATTERY_CHANGED`，**不是 SoC** |
| traceHealth / unmatched markers | `[]` / 0 |
| 窗口未完成帧 / 计数器重置 / 断连 | 3 / 无 / 无 |

### 预算判定

- 网络物理年龄：P95 279.69 ms ≤ 300 ms、P99 397.01 ms ≤ 500 ms → **仅这两项网络预算通过**。
- CPU/RAM 首次显示年龄：P95 334.42 / 334.21 ms > 300 ms → **FAIL**。500ms 的 RAM 采样天然可能在两次样本之间变旧；网络 P95 不能拿来证明所有字段。
- 严格 10Hz：网络 9.887628 Hz、接收 9.820156 Hz、physical NEW network 9.531574 Hz → **未完全达到**。
- 稳定 60FPS：59.295125 FPS、1% Low 29.935753 → **未完全达到**。

### Jank 口径

最终 jank 计数：2207 Buffer Stuffing、29 App Deadline Missed、2 App Deadline+Buffer、3 HAL+App Deadline、2088 None，合计 jank 比例 51.77%。**51.77% 不等于 51.77% dropped frames**；dropped buffer 为 0%，也不把所有 jank 都称为错过截止期。

DOM 688 与接收 687 的 1 条差属于边界待处理状态，不是把旧数据重新贴成新的 10Hz。

## 待最终（NOT TESTED）

| 项目 | 状态 |
|---|---|
| 最终正常使用 30 分钟 FPS | NOT TESTED |
| 24 小时连续观察 | CANCELLED_BY_USER |
| Windows 操作系统重启 | NOT_TESTED |
| Windows 睡眠/唤醒 | NOT_TESTED |
| 长期发热、数据库增长、更多机型 | NOT TESTED |

现有 30 分钟数字来自较早候选，已单独限定，不能当作最终候选的长期结果。用户已停止追加长时间测试，本轮不补做这些未测项。

## 最终 Agent 现有正常运行资源读数

在已运行进程上读取累计 CPU，无额外等待或启动长期测试：运行约 6188.68 秒，累计 CPU 154.875 秒，按 8 个逻辑处理器归一后的平均占用 **0.31282%**，读取时工作集 **108.78 MiB**、私有内存 **79.46 MiB**。这段既有进程寿命平均低于 2% 目标，但不是无断线稳定性验收，也不能证明整个期间的手机 FPS。方法为进程累计 CPU / 自进程启动经过的墙钟时间 / 逻辑处理器数，详见 [原始匿名读数](reviews/results/final-agent-existing-lifetime-cpu.json)。

## 证据与记录

- [reviews/results/final-phone-instrumented.json](reviews/results/final-phone-instrumented.json)
- [reviews/results/final-windows.json](reviews/results/final-windows.json)
- [reviews/results/preliminary-thirty.json](reviews/results/preliminary-thirty.json)
- [reviews/results/testing-scope.json](reviews/results/testing-scope.json)
- [reviews/INVOCATIONS.json](reviews/INVOCATIONS.json)

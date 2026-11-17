# Desk Companion 0.3.1 验证清单（Freebuff）

> **本文档未执行任何一项。** 该主机上 `run_terminal_command` 不可用（缺 Git Bash），无 Android 设备，无 release APK。以下每条均为待执行的**可复现步骤**，不是已获得的结果。凡是本文出现的数字，要么是待填的空格，要么是从代码推算的理论值（已标注"推算"）。
>
> 对应审计：`FREEBUFF_REVIEW_031.md`，条目编号（A1/B1/…）沿用该文。

## 0. 环境与前置

| 项 | 要求 | 记录 |
|---|---|---|
| PC | Windows x64，Agent 以 0.3.1 构建运行 | `Max3.Agent` 版本号 + 文件 SHA256 |
| 手机 | Android 8.0+，与 PC **OS 蓝牙配对完成** | `Build.MODEL` / `Build.DEVICE` / `VERSION.RELEASE` |
| 工具 | `adb`、`perfetto`（设备内）、Wireshark（解析 `btsnoop`） | 版本号 |
| 网络 | 全程飞行模式 + 仅蓝牙（隔离 Wi-Fi 通道干扰） | 是/否 |
| 电源 | 手机插电、`FLAG_KEEP_SCREEN_ON` 生效（常亮），PC 关闭睡眠 | 是/否 |

**基线声明**：每次测试开始前必须记录下面三条，否则结论不可比。

```
adb shell dumpsys package net.oxog.phonebox | grep -E "versionName|versionCode|debuggable"
# release 判定：debuggable=false 且 APK 经 apksigner 校验
apksigner verify --print-certs <apk>
```

---

## P0 — 阻断项（不通过则其余结论无效）

### P0-1 证明"这是 release 包"【对应 F1】

- **步骤**：`apksigner verify`；`aapt dump badging <apk>`；确认 `debuggable=false`、`WebView.setWebContentsDebuggingEnabled` 分支未开（`BuildConfig.DEBUG=false`）。
- **通过标准**：三项全部成立，且 APK 与源码 commit 可对应（记录 commit hash）。
- **证据**：`apksigner` 输出、`badging` 输出、commit hash。
- **注**：本次快照**不含任何 Gradle 文件**，所以该条目前状态为**未验证**。

### P0-2 证明性能测量工具存在【对应 F1/F3】

- **步骤**：在仓库中检索 `FrameMetrics`、`addOnFrameMetricsAvailableListener`、`Perfetto`、`Trace\.|beginSection`、`Choreographer`。
- **通过标准**：至少存在其一，且能在 release 变体中采集。
- **当前状态**：9 个快照文件内 **0 命中**（仅 REVIEW_REQUEST 自身）。若无任何命中，则第 6 节的性能项**必须标为"未测量"**，不得引用 UI 上的 FPS 卡片。

---

## 1. 吞吐与停等 ACK（A1–A4）

### T1-1 帧率与 RTT 分布（分开测量，禁止混用）

- **推算基线**：`T_cycle = 1000/3.76 = 266.0 ms`；若 RTT 均值为 241ms，则 `1/(0.489+0.025)=1.94 帧/s`（p95 帧的瞬时速率）。
- **步骤 A（帧率，无需改码）**：连接稳定 10 分钟后，从 PC 侧读取 `BluetoothTransport.Status().SentFrames` 两次，间隔 300s。
  `fps = (sent2 - sent1) / 300`。
- **步骤 B（RTT 分布，需外部证据，二选一）**：
  1. **HCI snoop**：Android 开发者选项开启"启用蓝牙 HCI 信息收集日志" → `adb pull /sdcard/btsnoop_hci.log` → Wireshark 按 RFCOMM 帧时间差统计往返。
  2. **临时打点**（不改协议）：在 `ReadMessages` 里把每次 `ackRoundTripUs` 追加到长度 3600 的环形数组并暴露；测完移除。
- **通过标准**：均值、p50、p95、p99、max **五个值齐全**，且标注 n（样本数）与统计窗口起止时间戳。
- **证据**：原始样本或 CSV + 统计脚本输出。
- **判定**：若 p95=489ms 而均值≈241ms，则**分布正确**（偏斜）；若均值也≈489ms，则 3.76fps 与代码矛盾，需查是否多数帧走了非 ACK 路径（`flowControl=false`）。

### T1-2 确认 ACK 模式确实生效

- **步骤**：PC 侧 `Status().FlowControl` 是否为 `true`；`AckRoundTripMs` 是否非 `null`。
- **通过标准**：`FlowControl=true` 且 `AckRoundTripMs` 非空。
- **理由**：若 `features` 协商失败，`flowControl=false`（`BluetoothTransport.cs:56`），此时无停等、无 ACK、无 RTT，A1 的整套算术**不适用**。这是最容易把结论做错的单点。

### T1-3 丢 ACK 的代价（A3）

- **步骤**：稳定通信中，在手机上关闭蓝牙 2 秒后重开（或在 PC 侧临时屏蔽入站 1 帧）。
- **通过标准（现状基线）**：记录"最后一次成功帧"到"恢复首帧"的间隔。按代码推算约 2s 超时 + 3s 重连 ≈ **5s**。
- **修复后标准**：同场景间隔 ≤ 1 个 RTT + 1 次重发（< 1s），且**不出现重连**（`sentFrames` 不清零）。
- **证据**：PC 日志 + 手机 `Companion` 事件表 `connection` 行 + 视频/秒表。

---

## 2. Windows 100ms 采样（B2–B5，C1）

### T2-1 快照节拍与抖动

- **步骤**：PC 侧采集 10 分钟的 `collector.Latest.Sequence` 变化时间戳（或直接读 `metrics.sequence` 的到达间隔）。
- **通过标准**：中位数 ≤ 105ms；p95 ≤ 210ms；**不得出现 > 300ms 的连续空洞**。
- **已知风险（B2）**：`Collector.cs:76` 的 80ms 门控会把合并 tick 拉成 200ms，p95 命中 200ms 属预期，需在报告中说明而非当作故障。

### T2-2 采集器故障注入（B1，最高优先级）

- **步骤**：
  1. 稳定运行 5 分钟后，用 Process Explorer / 远程调试在某一个 loop 内注入一次非 `OperationCanceledException` 异常（或临时在 `WifiLoop` 加 `throw new Exception("probe")`，**仅测试构建**）。
  2. 观察 PC 进程是否存活、`Latest.Sequence` 是否还在增长、手机是否仍显示"已连接"。
- **通过标准（修复后）**：注入后 ≤ 5s 内采集恢复，`Sequence` 继续增长，日志有 Error 记录。
- **当前代码预期（用于对照）**：进程退出（`StopHost`）**或**静默停帧且手机显示"已连接 + 数据已过期"。
- **证据**：进程存活截图、`Sequence` 时间线、手机截图。

### T2-3 `IfRow` 字段偏移交叉校验（B5）

- **步骤**：在测试构建启动时，取第一个 `GetIfTable2` 行的 `Alias`/`Description`，与 `GetIfEntry2` 返回的同 LUID 行的同字段逐字节比较；再比较 `InOctets/OutOctets` 是否与 `Get-NetAdapterStatistics`（或任务管理器网卡吞吐的 10 秒积分）同量级。
- **通过标准**：字符串完全一致；8 字节计数器单调且与外部工具换算的字节数误差 < 5%。
- **理由**：`Marshal.SizeOf<IfRow>()==1352` 是自我声明的常量，**不能**证明偏移正确。

### T2-4 RAM 结构与显示

- **步骤**：
  1. 记录 `MemorySample.UsedBytes/TotalBytes`。
  2. 与任务管理器"性能→内存→使用中/总数"对比（同一时刻，容许 ±3% 与 ±2s 时间差）。
  3. 在 UI **全文搜索**是否存在 GB 文本。
- **通过标准**：
  - 结构体：字节值与系统一致（结构体本身审计已确认 64 字节、`Length` 每次赋值，预期通过）。
  - 显示：若验收要求"RAM 以 GB 显示"，则**当前必定不通过**——`main.js` 只渲染 `percent()`，`usedBytes/totalBytes` 上线但不显示。该项应按**需求缺失**处理，不是 bug。

---

## 3. Android 接收 / UI 队列（D1，D2）

### T3-1 背压缺失的可观测证明

- **步骤**：
  1. 稳定通信中，在 WebView 上执行一次重活：`adb shell am start` 打开历史弹窗（触发 `history()` 返回数百 KB + `drawHistory` 渲染 600+1440+100 行）。
  2. 同时读取 `window.companionDiagnostics().ageP95Ms` 与 `serverToReceiveP95Ms`。
- **通过标准（修复后）**：主线程卡顿期间 `ageP95Ms` 上升但**不出现单调累积**；卡顿结束后 2s 内回落。
- **当前代码预期**：`deliver()` 无合并，卡顿期间的 `post` 全部排队并逐条执行 ⇒ `ageP95Ms` 出现尖峰且恢复缓慢。
- **证据**：`ageP95Ms` 时间序列（每 2s 采一次，`evaluateJavascript` 取值）。

### T3-2 落库节流是否退化

- **步骤**：统计 10 分钟内 `samples` 表行数增量（`sqlite3` 或 `run-as` 拉取）。
- **通过标准**：≈ 600 行（1 行/秒）。若接近 2260 行（3.76×600），说明 `eventHistory` 一直非空，逐帧触发了事务。
- **依据**：`CompanionActivity.acceptState` 的节流条件含 `eventHistory.length()>0`。

### T3-3 数据年龄精度（时钟）

- **步骤**：人为让手机与 PC 系统时间相差 30s，重连，观察校时后 `sampleAgeAtReceiveMs` 是否收敛到 100–400ms 区间。
- **通过标准**：3 次采样内收敛；`clockUncertaintyMs` ≥ 实测 RTT/2。
- **已知偏差**：`age.coerceAtLeast(0.0)` 会抹掉负值 ⇒ **系统性少报**，报告中需注明"数据年龄为下界"。

---

## 4. 安全（E1–E3）

> 全部为离线/实验室验证，**不修改任何系统安全设置**，不导出真实 token。

### T4-1 重放与乱序

- **步骤**：用测试脚本在 HCI 层重放一帧已捕获的密文帧。
- **通过标准**：两端均抛 `Replay/sequence mismatch` 并断连；**不得**出现静默接受。
- **预期**：通过（`counter!=received+1` 两侧都硬校验）。

### T4-2 未配对设备

- **步骤**：用一台**未 OS 配对**的 Android 发起 RFCOMM 连接（或删除配对后重试）。
- **通过标准**：PC 侧不在 `PairedDevices` 内 ⇒ 根本不发起；手机侧 `bondState != BOND_BONDED` ⇒ `IOException`。

### T4-3 错误 token

- **步骤**：手机上把 `bluetoothIdentity(fp).token` 替换为随机 32 字节（测试构建）。
- **通过标准**：`proof` 校验失败，`Bluetooth identity rejected`，**且握手耗时无可测量的时间侧信道**（比较用 `FixedTimeEquals`/`isEqual`）。

### T4-4 特性降级留痕（E3）

- **步骤**：测试构建让手机回 `features=""`。
- **通过标准（修复后）**：PC 日志出现 Warning，UI/诊断可见"无 ACK 模式"。
- **当前预期**：静默进入无 ACK 模式，`AckRoundTripMs` 变 `null`，**无任何提示**。

### T4-5 密钥清零（尽力而为）

- **步骤**：认证失败路径下 dump 堆（测试构建）。
- **通过标准**：`root` 不残留明文密钥数组（预期当前**不通过**：`root.fill(0)` 只在成功分支）。
- **级别**：低，不阻断发布。

---

## 5. 发布版渲染性能（F1/F2）—— 必须用独立工具

> UI 的 `fps` 是 `requestAnimationFrame` 回调率，`missedVsyncEstimate` 是 `round(dt/16.7)-1` 的猜测（`main.js:112`），**两者都不能作为帧指标**；且 `visibilitychange` 会清零统计。以下三选一（推荐 A）。

### A. Macrobenchmark（首选）

- **要求**：新增 `benchmark` 模块，`FrameTimingMetric()`，release 签名与本包一致，`profileable`。
- **步骤**：连接稳定后 60s 稳态窗口，重复 5 次。
- **通过标准（建议阈值，非实测）**：`frameDurationCpuMs` p95 ≤ 16.7ms（60Hz）；`frameOverrunMs` p95 ≤ 0；jank 帧占比 < 5%。
- **证据**：`benchmarkData.json` 原始文件 + 设备型号 + 构建 hash。

### B. `dumpsys gfxinfo`

```
adb shell dumpsys gfxinfo net.oxog.phonebox reset
# 等待 120s 稳定通信
adb shell dumpsys gfxinfo net.oxog.phonebox framestats
```
- **通过标准**：从 `framestats` 的 `FrameCompleted - IntendedVsync` 自行计算 p50/p95/p99 与 jank 比例；**必须给出计算公式与样本数**。

### C. Perfetto

```
adb push perfetto.cfg /data/local/tmp/
adb shell perfetto -c /data/local/tmp/perfetto.cfg --txt -o /data/misc/perfetto-traces/dc.perfetto-trace
adb pull /data/misc/perfetto-traces/dc.perfetto-trace
```
- 数据源至少包含 `sched/sched_switch`、`gfx`、`android.surfaceflinger.frame`、`power/battery_status`。
- **通过标准**：trace 中能定位每个 `doFrame` 的实际耗时与 missing-frame 原因；给出至少 3 个具体的超时 slice 归因。

**共同要求**：三项都必须在 **release（`debuggable=false`）** 包上跑；debug 包数字不作数。

---

## 6. 24 小时长稳（F4）

### T6-1 采集与留存

- **步骤**：连续 24h，每 60s 记一行 CSV：`timestamp, transport, connected, seq, ageP95, serverToReceiveP95, ackRttLast, sentFrames, sequenceGaps, battery%, rss(PC), rss(app)`。
- **通过标准**：
  - 无 > 30s 的连续无数据空洞（若出现，必须能对应一次 `connection` 事件）；
  - PC `rss` 与 app `rss` 24h 内增幅 < 20%（判内存泄漏）；
  - `samples` 行数在 6h 后进入稳态（约 21600 上限滚动）；
  - `minutes` 行数 ≈ 1440；
  - `events` ≤ 10000。
- **证据**：CSV + `dumpsys meminfo net.oxog.phonebox` 首末两份 + SQLite 行数快照。

### T6-2 统计窗口必须自证

- **步骤**：记录每一次重连的时间戳，并统计 24h 内重连次数。
- **通过标准**：报告 p95 时必须同时给出"统计覆盖时长"与"重连次数"。因为 `measuredRoute` 变化会清空 `ages/receiveAges/serverDelays/sequenceGaps`，**且 `visibilitychange` 会清空 `frames/frameIntervals`**，所以一次 resume 或一次重连就让此前样本失效。
- **硬性要求**：若重连次数 > 0，则"24 小时 p95"这一说法**不成立**，只能表述为"若干会话的分段统计"。

### T6-3 续航与热

- **步骤**：`adb shell dumpsys batterystats --reset` 后跑 24h，结束导出。
- **通过标准**：记录 `mAh` 消耗与最高温度；**必须注明测试时常亮屏**（`FLAG_KEEP_SCREEN_ON`），结论不可外推到日常使用。

---

## 7. 一键验收表（打印用）

| ID | 项 | 结果 | 证据文件 | 备注 |
|---|---|---|---|---|
| P0-1 | release 包可验证 | ☐ | | 快照中无 Gradle |
| P0-2 | 测量工具存在 | ☐ | | 当前 0 命中 |
| T1-2 | `FlowControl=true` | ☐ | | 前置阻断 |
| T1-1 | 帧率 + RTT 五分位 | ☐ | | 禁止混用均值/p95 |
| T1-3 | 丢 ACK 恢复时间 | ☐ | | 现状预期 ≈5s |
| T2-1 | 100ms 抖动 p95 | ☐ | | 预期 200ms 命中 |
| T2-2 | 采集器故障自愈 | ☐ | | **最高优先级** |
| T2-3 | IfRow 偏移交叉校验 | ☐ | | |
| T2-4 | RAM 与系统一致 / GB 显示 | ☐ | | 显示项预期不通过 |
| T3-1 | UI 队列不累积 | ☐ | | |
| T3-2 | 落库 ≈1 行/秒 | ☐ | | |
| T3-3 | 校时收敛 | ☐ | | 年龄为下界 |
| T4-1 | 重放被拒 | ☐ | | |
| T4-2 | 未配对被拒 | ☐ | | |
| T4-3 | 错误 token 被拒 | ☐ | | |
| T4-4 | 降级留痕 | ☐ | | 预期不通过 |
| T5 | release 帧指标（A/B/C 任一） | ☐ | | 无工具则填"未测量" |
| T6-1 | 24h 无长空洞 / 无泄漏 | ☐ | | |
| T6-2 | 统计窗口自证 | ☐ | | 重连>0 则 p95 无效 |
| T6-3 | 24h 续航与温度 | ☐ | | 常亮屏工况 |

**填写纪律**：任一单元格只能填 `通过 / 不通过 / 未执行`，不允许填"应该可以"。凡未采集到原始证据的项，一律记 `未执行`。

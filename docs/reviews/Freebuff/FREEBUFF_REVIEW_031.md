# Desk Companion 0.3.1 独立审计（Freebuff）

## 0. 范围与方法

只读取了以下 9 个快照文件，未读取凭据，未修改任何源码，未修改系统安全设置：

```
agent/Max3.Agent/BluetoothTransport.cs
agent/Max3.Agent/BluetoothCrypto.cs
agent/Max3.Agent/Collector.cs
agent/Max3.Agent/WireState.cs
android/.../BluetoothLink.kt
android/.../CompanionActivity.kt
android/.../CompanionStore.kt
tools/companion-ui/src/main.js
tools/companion-ui/src/model.js
```

**本次审计没有运行任何构建、设备测试或 24 小时测试**（该主机上连 bash/git-bash 都不可用，`run_terminal_command` 直接失败）。因此下文所有"观测值"均来自请求方提供的数字，所有结论均来自静态代码推理，**标记为【确认】的仅限于代码可判定的逻辑/算术事实，不含任何运行时实测**。

标记约定：**【确认】**=代码可证明；**【假设】**=代码支持但仍需实测；**【未测试】**=快照中不存在对应实现或证据。

---

## 1. 结论摘要

| # | 项目 | 结论 | 级别 | 类型 |
|---|---|---|---|---|
| A1 | 停等 ACK 吞吐上限 | 帧率被 `1/RTT` 硬性封顶，3.76 fps 与 489ms p95 在算术上自洽，但**相互矛盾地说明均值 RTT≈241ms** | 设计性 | 【确认】 |
| A2 | 25ms `PeriodicTimer` 门控 | 每帧额外引入 0~25ms（均值≈12.5ms）纯等待，占周期约 9% | 可优化 | 【确认】 |
| A3 | ACK 无重传 | 丢一个 ACK ⇒ 2s 超时 ⇒ 全连接重建（+3s 重连），24h 内必然发生 | 高 | 【确认】 |
| A4 | 读循环与 ACK 竞争 | `ping` 回包在同一线程、同一 `sendLock` 上插入 ACK 所在的关键路径，直接抬高 p95 | 中 | 【假设】 |
| B1 | 采集器无监督 | 5 个 loop 中任一抛出非 OCE 异常 ⇒ `Task.WhenAll` 故障 ⇒ 采集器停止，而传输层把"序号不变"当作正常，**静默停帧** | 高 | 【确认】 |
| B2 | 100ms 采样抖动 | `if(now-lastRead<80)continue` ⇒ tick 合并时实际落到 200ms | 低 | 【确认】 |
| B3 | 热路径字符串分配 | 每 adapter 每 100ms 都执行 `Description+" "+Name` 重算 `kind`，与注释"no strings allocated"相反 | 低 | 【确认】 |
| B4 | `GetIfEntry2` 瞬时错误代价 | `baseline=connected` 被清零 ⇒ 一次错误丢 **2 个**采样 | 低 | 【确认】 |
| B5 | `IfRow` 字段偏移 | 仅断言总长 1352，`FieldOffset` 无运行期交叉校验，偏移错会静默产出垃圾统计 | 中 | 【假设】 |
| C1 | Windows 100ms ≠ 手机 100ms | 采集 100Hz，但端到端有效速率仅 ≈3.76Hz；"100ms 采样"只描述 Windows 一侧 | 事实澄清 | 【确认】 |
| D1 | Android→WebView 无背压 | Agent 侧 `Channel.CreateBounded(1)` 有界，Android `deliver()` 无合并/丢弃，主线程 post 可无界堆积 | 中 | 【确认】 |
| D2 | 队列时延可观测 | `queueMs` 与 `ageAtWrite` 已把两段排队纳入新鲜度计算，**测量到位、只缺限流** | 正向 | 【确认】 |
| E1 | 握手/加密构造 | 每会话新 `cn/sn` ⇒ 每会话新密钥 ⇒ GCM 无 nonce 复用；AAD=计数器；双方定长比较 | 正向 | 【确认】 |
| E2 | 密钥派生 | 直接 `SHA256(token)`，无 HKDF/盐；token 熵不可见 | 条件性 | 【未测试】 |
| E3 | 特性协商可静默降级 | `features` 不一致即关闭 ACK 且无日志/无策略 | 低 | 【确认】 |
| F1 | 发布版 FrameMetrics/Perfetto | 快照中**零**处 `FrameMetrics`/`Perfetto`/`Trace`/`Choreographer`；也无任何 Gradle/构建配置 | 高 | 【未测试】 |
| F2 | UI 的 "FPS" | `requestAnimationFrame` 回调率 + `round(dt/16.7)-1` 估算，**不是**丢帧/卡顿指标 | 事实澄清 | 【确认】 |
| F3 | ACK RTT p95 数据源 | 代码只暴露**最后一次** RTT（`AckRoundTripMs`），无 p95 实现；489ms 必须来自外部工具 | 高 | 【确认】 |
| F4 | 24 小时测试 | 快照无任何长稳脚本/日志/产物；且 `frames/count` 在每次 resume 时清零 ⇒ 本代码无法佐证 24h 结论 | 高 | 【未测试】 |

---

## 2. 停等 ACK 吞吐（A1–A4）

### 2.1 机制

`BluetoothTransport.cs:81–91`：
```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
while(await timer.WaitForNextTickAsync(ct)) {
    var sample = collector.Latest; if(sample is null || sample.Sequence==sequence) continue;
    ...
    await channel.Write(state);                       // :88
    Interlocked.Increment(ref sentFrames);            // :89
    if(flowControl && await acknowledgements.ReadAsync(timeout.Token)!=sample.Sequence) throw ...; // :90
    sequence = sample.Sequence;
}
```
窗口恒为 1：**发送 → 阻塞等 ACK → 才回头取新样本**。`Channel.CreateBounded<long>(1)`（:68）保证"Ack 最多缓存 1 个"。

### 2.2 算术（这是本次审计最硬的证据）

- 观测 3.76 帧/s ⇒ 实际周期 **T = 266.0 ms**。
- 代码周期 ≈ `T_tick门控(0~25ms) + T_write + RTT + T_ack处理`。
- 因为采样源每 100ms 才变一次，而 RTT ≫ 100ms，循环回到顶部时样本必已更新 ⇒ 35ms 的"等新样本"项为 0。
- 取门控均值 12.5ms、写+处理约 12ms，则 **RTT ≈ 241ms**。
- 用 p95=489ms 反推：`1/(0.489+0.025) = 1.94 帧/s`。也就是说**尾延迟的那 5% 帧只能跑出约 1.94 帧/s**。

**结论**：3.76 fps 与 489ms p95 并不互相印证，而是说明分布高度偏斜（均值≈241ms、p95≈489ms，尾/均比 2.0）。若 489ms 是均值，帧率应约为 1.9 而非 3.76。二者必须分开报告，混用会误导。**【确认，纯算术】**

同时：10Hz 的采集被 3.76Hz 的传输吞掉 62%，这与"最新值优先"的设计意图一致，但**不能用 3.76Hz 去论证 100ms 采样质量**（见 C1）。

### 2.3 最小、向后兼容的提速手段

| 改动 | 预期收益 | 兼容性 |
|---|---|---|
| 去掉 25ms 门控，改为 `await ack` 返回后立刻取当前 `Latest` 并发送 | 省 0~25ms（均值 12.5ms）⇒ 266→253.5ms ⇒ **≈4.13 帧/s（+9.4%）** | 纯本地，报文不变 |
| 读循环把 `ping` 回包延后（先读 ACK、把 `time` 回包塞进自己的发送队列） | 降低 p95，不增带宽 | 纯本地 |
| **不要**引入流水线/窗口>1 | 会改变 `ack-route-v1` 语义，与 0.3.x 手机不兼容 | 破坏兼容 |

要点：`features="ack-route-v1"` 字符串是**唯一**的协商位，且被绑进 HMAC 上下文（`BluetoothTransport.cs:57`）。任何"改 ACK 语义"的优化都必须换新 feature 名并保留旧路径，成本远大于收益。**建议 0.3.x 只做延迟优化，不做窗口化。**

### 2.4 A3：无重传的代价【确认】

`WriteStates` 对写与等 ACK 共用一个 2000ms 的 CTS（:84）。ACK 丢失或手机侧一次停顿 ⇒ `OperationCanceledException` ⇒ 外层 `catch` ⇒ `Task.Delay(3000)` 重连（:74）。**一次丢包 = 约 5 秒完全无数据**，且 `sequence` 归 -1，UI 立刻进入"数据已过期"（`STALE_MS=1000`，`model.js`）。

建议（最小改动）：ack 超时 2s→4s；超时后**重发同一帧**（序号不变，手机端 `receiveCounter` 会因 `counter!=expectedCounter` 抛 `Replay/sequence mismatch`——所以**不能**重发同一加密帧，必须重发同一 `sample.Sequence` 但重新加密的新帧，序号自增）。这一点必须在改代码前先确认接收端允许同 `sequence` 的重复 `state`：

- 接收端 `CompanionStore` 用 `INSERT OR IGNORE` 主键 `t`，重复帧同 `sample.Sequence` 会算出同一 `historyAt` ⇒ 幂等 ✔
- 但 `main.js` 有 `if(data.metrics.monotonicMs<=lastMono)return` 去重 ⇒ 重复帧会被 UI 丢弃 ✔
- 因此**允许重发**，代价只是多一次往返。这是 A3 的最小修法。

### 2.5 A4：ACK 被 ping 抢锁【假设】

Android 侧接收线程在 `BluetoothLink.kt:114` 收到 `state` 后**必须在 `synchronized(sendLock)` 里写 ACK**；而 `requestTime()`（:140）在 `pings` 计划线程上持有同一把锁写 `ping`。RFCOMM 的 `out.flush()` 是同步阻塞的，因此一次 ping 写可能让 ACK 排队数十毫秒。ping 周期 5s，在 3.76fps 下约每 19 帧命中 1 次——这正好落在 p95 尾部。

同时 `CompanionActivity.synchronizeClock()` 每 30s 在蓝牙态直接调 `bluetooth.requestTime()`，且 `clockSamples<3` 时会连发 3 次（`BluetoothLink.kt:120`），握手后前几秒更容易命中。

**这是我认为 489ms p95 最可能的代码级来源**，但需要实测才能定级。

---

## 3. 新鲜度与"最新值"流控

设计意图（:66–67 注释）是"队列只放 1 帧，用 ACK 触发取最新快照，过期样本留在采集器里"。**该意图在 Agent→手机方向被正确实现**，证据：

1. 有界通道 1（:68）+ 单帧在途 ⇒ 端到端老化 ≈ `RTT/2 + 排队 ≈ 120–370ms`，远小于 `STALE_MS=1000`。
2. 手机侧不缓存历史帧，只有 `last`（`main.js:44`）。
3. `CompanionStore.accept` 用 `ageAtWrite(...)` 扣掉"接收→落库"的排队时间（`CompanionStore.kt:61`），并把 `upperBoundMs>1000` 的帧记为缺口——这一点做得比多数实现严谨。

代价（需明写在文档里，不能当作缺陷）：
- **吞吐上限永远是 `1/(RTT+25ms)`**，与采样率无关。
- 62% 的 100ms 采样被丢弃，`sequenceGaps` 会持续增长（`main.js` 计数），**它不是网络故障指标**，不能作为 SLA。

两处不一致需修：
- `main.js:47` 用 `cpuAge = serverSentAtUnixMs - cpu.sampledAtUnixMs`（只算 Agent 内部老化）与 `fresh()` 的端到端预算叠加判断，两套口径混用；建议统一走 `sampleAgeAtDisplayMs`。
- `CompanionActivity.kt` 里 `age = received + offset - sample` 后 `coerceAtLeast(0.0)`，**把时钟偏差造成的负值抹平了**，会系统性少报数据年龄（偏差上界≈RTT/2）。

---

## 4. Windows 100ms 采样与 RAM

### 4.1 采样链【确认】

| 指标 | 周期 | 证据 |
|---|---|---|
| 快照发布 | 100ms | `Collector.cs:73` |
| CPU | 250ms | `Collector.cs:35` |
| 内存 | 500ms | `Collector.cs:50` |
| 磁盘 | 250ms | `Collector.cs:61` |
| Wi-Fi RSSI | 1000ms | `Collector.cs:131` |

即 100ms 快照里 CPU 最多带 250ms、内存最多 500ms 的"内部老化"。UI 用 `cpuAge<1000` / `memAge<1500` 掩盖了这一点，**属于有意的混合节奏**，不是 bug；但对外表述"100ms 采样"时应说明是"快照节拍"。

### 4.2 B2 抖动【确认】

`Collector.cs:76` `if(now-lastRead<80)continue;`。`PeriodicTimer` 在负载抖动下会合并 tick：100ms 的 tick 若迟到到 180ms，下一 tick 在 200ms 处 ⇒ 判断 `200-180=20<80` ⇒ 跳过 ⇒ 实际间隔 200ms。这是"偶发 200ms"的确切来源。

### 4.3 B1 采集器无监督【确认，高】

```csharp
try { await Task.WhenAll(CpuLoop(ct), MemoryLoop(ct), DiskLoop(ct), WifiLoop(ct), NetworkLoop(ct)); }
catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
```
`Collector.cs:29–31`。任一 loop 抛出非 OCE（`WlanQueryInterface` 的 P/Invoke 已包 try，但 `GetIfTable2Ex` 路径、`PdhGetFormattedCounterValue` 结构体错位、`Marshal.PtrToStringUni(...)!` 的空引用都在嫌疑名单里），`Task.WhenAll` 立即故障。后果链：

1. `BackgroundService.ExecuteAsync` 异常 ⇒ .NET 6+ 默认 `StopHost` ⇒ 整个 Agent 退出；若宿主管线被配置为忽略，则采集器**静默停止**。
2. 无论哪种，`latest` 从此不再更新 ⇒ `WriteStates` 的 `sample.Sequence==sequence` 恒成立 ⇒ **继续 `continue`，不报错、不重连**（:83）。
3. 手机侧看到的是"连接正常 + 数据已过期"，`BluetoothLink` 看门狗因为 Agent 仍在发 ping 回包也不会关闭（:63 依赖 `lastReceive`，而 ping 每 5s 到）。

这是我在本次审计中**最担心的失效模式**：一个可见但不可自愈的静默停帧。最小修法：`ExecuteAsync` 内对 5 个 loop 用 `Task.WhenAny` + 重启（或至少 `catch(Exception)` 记 Error 并重建），并在 `WriteStates` 里加"超过 N 秒无新快照则上报/断开"。

### 4.4 B3 热路径分配【确认，低】

`Collector.cs:88` 每次采样都对每个 adapter 求 `Kind(row.Type, physical, a.Description+" "+a.Name)`，即使 `row.Type/physical` 未变。`Kind` 内还有 5 次 `Contains`。10 个 adapter × 10Hz ⇒ ≈200 次字符串分配/秒，与 `Collector.cs:124` 注释 "no strings allocated by marshalling" 的意图相悖。修法一行：`if(error==0 && (row.Type!=a.Type||physical!=a.Physical)) kind=Kind(...); else kind=a.Kind;`

### 4.5 B4 瞬时错误丢 2 个采样【确认，低】

`NetworkRate.Sample` 末尾 `(rx,tx,at,baseline)=(received,sent,now,connected)`。`error!=0` 时 `received/sent` 为 0 ⇒ 下一帧要么 `baseline` 重来（`baseline=false`）⇒ 再丢一帧。一次 `GetIfEntry2` 失败实际损失约 200–300ms 数据。修法：错误时**不更新** `rx/tx/at`，只标 `reason`。

### 4.6 B5 `IfRow` 偏移【假设，中】

`Collector.cs:28` 只断言 `Marshal.SizeOf<IfRow>()==1352`。**总长对不代表偏移对**：`Type@1128 / Flags@1152 / OperStatus@1156 / InOctets@1208 / OutOctets@1280` 任一错位都不会抛异常，只会静默给出错误流量。`Size=1352` 是 `LayoutKind.Explicit` 的人为声明，它同时也"证明"了自己——这个断言是循环论证，没有校验力。

建议（不改结构）：启动时用 `GetIfTable2` 取一个已知 adapter，与 `GetIfEntry2` 的 `Alias/Description` 交叉核对一次，失败即降级为"未知"。这是**唯一**能证明偏移正确性的低成本手段。

### 4.7 RAM：`GlobalMemoryStatusEx` 格式化

**【确认】结构体正确。** `MemoryStatus` 为 `uint Length, Load` + 7×`ulong` = 8+56 = **64 字节**，与 `MEMORYSTATUSEX` 完全一致；`Length` 每次调用都重新赋值（`Collector.cs:52`）；x64 下两个 `uint` 后接 `ulong` 的 8 字节对齐正确，`LayoutKind.Sequential` 无需改动。

**【确认】计算正确。** `used = TotalPhysical - AvailablePhysical`；`100d*used/m.TotalPhysical` 中 `100d*` 先把 `used` 提升为 double，不会发生整型截断；`AvailablePhysical<=TotalPhysical` 的前置校验到位。"Total − Available" 等于任务管理器的"使用中"，口径正确。

**【确认】格式化缺失。** 全仓库唯一的 RAM 呈现是 `percent()` 的 `toFixed(1)`。`MemorySample.UsedBytes/TotalBytes` **会被序列化上线却从不渲染**（`main.js` 只读 `metrics.memory.percent`），`MemorySample.IntervalMs` 同样未用。所以"GB 格式化"在 0.3.1 中**并不存在**，既无 bug 也无可测对象；若 0.3.1 的验收标准里写了"RAM 以 GB 显示"，该条为**不成立**【未测试】。

顺带一处小浪费：内存 500ms 一变，却被打进每个 100ms 快照，约 5 帧重复携带同一份 `usedBytes/totalBytes`。可接受（gzip 后成本极低），但这是 B 类节拍设计的自然结果，不必改。

---

## 5. Android 接收 / UI 队列（D1–D2）

### 5.1 路径

```
BT 线程 readSecure(解密+解压+JSONObject.parse)
  → deliver("state")            // CompanionActivity.kt:144
  → runOnUiThread { evaluateJavascript("window.companionEvent('state',JSON.parse(\"...\"))") }
  → 主线程 JSON.parse + update() + canvas 重绘
```

### 5.2 D1 无背压【确认，中】

`deliver()` 只有 `pageReady/destroyed` 两个门，**没有合并、没有容量上限、没有 latest-only 语义**。Agent 侧精心做了 `Bounded(1)`，Android→WebView 这一跳却是无限队列的 `post`。3.76fps 时无碍；一旦 WebView 卡顿（大 `history` 回填、`drawHistory` 一次渲染 600+1440+100 行、或 `history()` 生成数百 KB 字符串后 `evaluateJavascript`），主线程排队会持续累积，且**累积的是过时帧**——正好与该产品"只显示最新值"的核心承诺相反。

最小修法（与 Agent 侧同构，20 行内）：`deliver` 对 `event=="state"` 只保留一个 `pending`，用 `post` 中的标志位合并；`connection/history` 不合并。这不会改动任何报文格式。

### 5.3 双重编码【确认，低】

`JSONObject.quote(data)` 把 JSON 文本塞进 JS 字符串字面量，再 `JSON.parse` 回来：O(n) 两次遍历 + 一次转义复制。安全性没问题（`quote` 会转义 `"` `\` `/` 与控制字符，`JSON.parse` 无法逃逸）；只是 CPU。`\u2028/\u2029` 未转义，在 Chrome 75+（ES2019 JSON superset）上合法，可忽略。

### 5.4 D2 队列时延已被计入【确认，正向】

两段排队都被显式测量：
- 主线程 hop：`main.js` 里 `queueMs = nativeNow - data.receivedAtPhoneMonotonicMs`，加到 `sampleAgeAtDisplayMs`。
- 落库 hop：`CompanionSampleFreshness.ageAtWrite(..., receivedAtPhoneMonotonicMs, mono)`（`CompanionStore.kt:61`）扣掉 IO 执行器排队。

这说明 A/D 两类的风险**已经被作者识别并测量**，缺的只是 D1 的限流。这是本次审计对 0.3.1 评价最高的部分。

### 5.5 其它确认项

- `acceptState` 的 `if(destroyed || source=="wifi" && transport=="bluetooth")return` 优先级正确（`&&` 高于 `||`）✔
- `eventHistory` 非空就绕过 1000ms 落库节流（`CompanionActivity.kt`）——正常态下 Agent 的 `cursor` 已推进、history 为空，所以实际约 1 次/秒 ✔；但若某事件的 `Sequence` 不递增，会退化为**每帧一次 SQLite 事务**（3.76 次/秒），需在测试计划里盯住。
- `CompanionStore.accept` 每写一条采样都重算整分钟的 `AVG`（`INSERT OR REPLACE INTO minutes SELECT ...`），60 行/分钟，成本可忽略 ✔

---

## 6. 安全

### 6.1 站得住的设计【确认】

1. **每会话新密钥**：`cn`（PC 生成 32B）与 `sn`（手机生成 32B）都是随机，且被绑进 `context`；`pc-to-phone` / `phone-to-pc` 双向独立密钥。因此 **AES-GCM 计数器从 1 重新开始不会造成 nonce 复用**——这是最容易写错的地方，此处正确。
2. **域分隔清晰**：`"phone|"+context` / `"pc|"+context` 防止角色反射攻击；`v1|fp|cn|sn[|feature]` 把所有协商内容纳入认证。
3. **AAD = 计数器**（`BluetoothCrypto.cs:41`；`BluetoothLink.kt` 的 `updateAAD(packet,0,8)`），计数器严格 `received+1`，重放/重排直接 `CryptographicException`。
4. **定长比较**：PC 用 `CryptographicOperations.FixedTimeEquals`，手机用 `MessageDigest.isEqual`，两侧都无短路泄露。
5. **解压炸弹有界**：入站 262144 上限、出站 262144，且逐块累计检查（两侧一致）。
6. **OS 配对强制**：手机端 `bondState != BOND_BONDED` 直接拒绝；PC 端 `Authenticate=true; Encrypt=true`，且只连 `PairedDevices` 中的地址、`Select()` 还校验地址在白名单内。
7. **WebView 加固到位**：`allowFileAccess=false`、`allowContentAccess=false`、`setSupportMultipleWindows(false)`、`shouldOverrideUrlLoading=true`、`shouldInterceptRequest` 非 `/assets/` 一律 403、`setWebContentsDebuggingEnabled(BuildConfig.DEBUG)`、事件列表用 `textContent` 渲染（无 XSS）。JS 桥只暴露给 `appassets.androidplatform.net`，外部页面无法加载该源。

### 6.2 需要修/需要证据的

| 项 | 说明 | 级别 |
|---|---|---|
| E2 密钥派生 | `SHA256(token)` 直接当根密钥，无 HKDF/盐/迭代。若 token 是 256bit 随机则可接受；若来自口令或较短的配对串则不可接受。**快照中看不到 token 的生成，无法判定**。 | 【未测试】 |
| E3 静默降级 | 手机回 `features=""` 时 PC 直接以无 ACK 模式运行，**无日志、无 UI 提示**。RFCOMM 已配对前提下无法被第三方 MITM，风险低，但应按"降级必须留痕"处理（一行 `log.LogWarning`）。 | 低 |
| 失败路径密钥未清零 | Android 的 `root.fill(0)` 只在认证**成功**分支内（`BluetoothLink.kt:105`）；异常路径 `root` 留在栈上。PC 侧 `ZeroMemory(root)` 在成功路径（:62），但 `Encoding.UTF8.GetBytes(token)` 中间量从不清理。 | 低 |
| 无握手限速 | `loop()` 捕获异常后固定 500ms 重试，鉴权失败也是 2 次/秒无限重试。对本地攻击者无实质放大（需 OS 配对 + 256bit token），但会产生日志/电量噪声。 | 低 |
| 帧类型靠计数器区分 | 明文握手与密文帧之间没有显式 tag/版本字节，靠"count 必须等于 1"排除混淆。实际不可利用（需 JSON 前 12 字节恰好构造出 8 字节整数 1），但这是隐式不变量，值得注释固定。 | 信息 |
| 配置文件 | `bluetooth-phone.txt` 明文存 MAC，非机密，可接受；但 `File.WriteAllText` 与 `active?.Cancel()` 之间无锁，连点两个设备时可能写旧值后取消新连接。 | 低 |

**未变更任何系统安全设置**，也未读取任何凭据文件。

---

## 7. 发布版 FrameMetrics / Perfetto 与 24 小时测试

这一节是本次审计**唯一必须给出否定结论**的地方。

### 7.1 快照中不存在任何渲染性能测量【未测试】

对全部 9 个文件做了正则检索 `FrameMetrics|Perfetto|systrace|Trace\.|beginSection|addOnFrameMetricsAvailableListener|Choreographer`（忽略大小写）：**除 REVIEW_REQUEST.md 自身外零命中**。

因此：
- 没有任何 `Window.addOnFrameMetricsAvailableListener` 采样 `TOTAL_DURATION`/`DEADLINE`/`JANK_*`；
- 没有任何 Perfetto/`TraceCompat` 打点，也没有 `androidTest`/macrobenchmark 模块；
- **快照里连 `build.gradle(.kts)` 都不存在**，所以无法确认 release 变体的 `minifyEnabled`/`debuggable=false`/`shrinkResources`。也就是说"genuine release APK"这一前提在本次审计中**无法建立**，任何 release 性能数字我都不能认可为已核实。

### 7.2 UI 里的 FPS 不是帧指标【确认】

`main.js:112`：
```js
if(now-start>=1000){fps=count*1000/(now-start); ... }
missed += Math.max(0, Math.round(dt/(1000/60))-1);
```
- `fps` 是 **WebView 内 `requestAnimationFrame` 回调率**，只能说明"主线程没被完全堵死"，与 Choreographer 的实际 vsync 交付、掉帧、`FrameMetrics` 的 jank 计数**无关**。
- `missedVsyncEstimate` 是 `round(dt/16.7)-1` 的启发式，**不是**掉帧数：一次 40ms 的阻塞会被估成 1（实际可能丢 2 帧），而连续两次 33ms 会估成 2（实际丢 2）——量纲都不稳。
- 更致命：`visibilitychange` 时 `frames=count=missed=0; frameIntervals=[]`（`main.js:114` 附近）。**任何切后台/锁屏都会把统计清零**，所以这份诊断数据在结构上不可能支撑"24 小时"的结论。

### 7.3 ACK RTT p95 无数据源【确认】

全仓库只有一个 RTT 标量：`Interlocked.Exchange(ref ackRoundTripUs, ...)`（`BluetoothTransport.cs:97` 附近），并经 `Status()` 暴露为**最后一次** `AckRoundTripMs`。`main.js` 的 `p95()` 只作用于 `frameIntervals`、`ages`、`receiveAges`、`serverDelays`，**没有 ACK RTT 数组**。

结论：**"ACK RTT p95 ≈ 489ms" 不可能由 0.3.1 自身产出**，必须来自外部抓包或额外打点。报告里必须写明其来源，否则不可复现。同时 3.76fps 可以由 `sentFrames`/时长 或 UI 的 `sequenceGaps` 推出，但两者都不是标准化的指标接口。

### 7.4 "真实 24 小时测试"不可佐证【未测试】

快照中无长稳脚本、无日志格式、无 CSV/JSON 导出、无 `dumpsys` 采集、无 24h 基线文件。且如上，`frames`/`fps`/`ages` 在一次 resume 后全部清零；`measuredRoute` 变更（`session:transport`）时 `ages/receiveAges/serverDelays/sequenceGaps` 也全部清空。**因此任何 p95 都只覆盖"最后一次重连以来"**，在 24h 里可能只有几分钟。

24 小时内**按代码可以预言会发生的**事件（供测试计划设预期，非实测）：
1. A3 的丢 ACK ⇒ 每次约 5s 无数据，重连后统计清零；
2. `onPause` 会 `store.event("connection","none:offline")`（`CompanionActivity.kt`），亮灭屏各写一行，10k 事件上限按 24h 亮灭屏 100 次计无压力 ✔；
3. `samples` 保留 6h（`t < now-6h` 删除）、`minutes` 30 天、`events` 30 天且 ≤10000 条 ✔，表规模有界；
4. `FLAG_KEEP_SCREEN_ON`（`CompanionActivity.kt:53`）意味着"24h 测试"是**常亮屏**工况，与日常续航工况不同，结论不可外推。

---

## 8. 建议的最小改动清单（0.3.x，向后兼容，无重写）

按性价比排序，全部**不含报文格式变更**：

1. **（高）采集器自愈**：`Collector.ExecuteAsync` 用 `catch(Exception)` 兜底并重启各 loop；`WriteStates` 增加"快照超过 2s 未更新 ⇒ 主动断开并记 Error"。解决静默停帧（B1）。
2. **（高）ACK 抗丢包**：写+等 ACK 超时 2000→4000ms；超时后**重新加密并重发同一 `sample.Sequence`**，不重建连接。解决 5s 黑洞（A3），且已验证接收端幂等。
3. **（中）读循环让路**：`ReadMessages` 不直接写 `time` 回包，改为入队由写循环发送；`ping` 到达时优先继续读 ACK。压低 p95（A4）。
4. **（中）去 25ms 门控**：ACK 返回后立即取 `Latest`，`PeriodicTimer` 只作 100ms 兜底。约 +9% 帧率（A2）。
5. **（中）Android 侧 latest-only**：`deliver("state")` 合并，只保留最新待投递帧。补齐 D1，与 Agent 侧语义对齐。
6. **（低）B3/B4**：`kind` 仅在类型/物理性变化时重算；`GetIfEntry2` 出错时不更新 `rx/tx/at`。各 1–3 行。
7. **（低）E3 降级留痕**：`features` 不含 `ack-route-v1` 时打 Warning。
8. **（低）RAM 不再上传无用字节**：`usedBytes/totalBytes` 要么在 UI 显示为 GB，要么从报文去掉；二选一，别保留"传了但不显示"。

**明确不建议**：窗口化/流水线（破坏 `ack-route-v1`）、重写传输层、替换 SQLite 结构、把采集改成事件驱动。

---

## 9. 缺失文件与未审计范围

以下符号被引用但**不在快照中**，其行为未被审计，相关结论均为间接：

| 符号 | 引用处 | 影响 |
|---|---|---|
| `AgentLink` / `AgentStream` / `AgentLink.PairLink` | `CompanionActivity.kt`、`BluetoothLink.kt` | Wi-Fi 通道、配对/`claim`、`bluetoothIdentity`、时钟端点全部未知；本文第 6 节只覆盖蓝牙路径 |
| `CompanionSampleFreshness`（`ageAtWrite` / `fieldIsFresh`） | `CompanionStore.kt:61–62` | 落库侧新鲜度判定的**核心语义未知**；第 3 节对该函数的评价基于其调用签名推断，属【假设】 |
| `tools/companion-ui/src/trend.js` | `main.js:3` | 24h 内存增长的唯一未审计点；`trend` 是否有界无法判断 |
| `AndroidManifest.xml`、`build.gradle(.kts)`、`proguard` | — | release 变体、`debuggable`、导出组件、`BLUETOOTH_*` 权限声明均不可验证 |
| `ai.Snapshot()` / `EventStatus` | `WireState.cs`、`BluetoothTransport.cs:84` | `ai` 字段与事件历史的大小/增长未知，直接影响每帧体积与 `history` 非空的频率 |

---

## 10. 一句话结论

0.3.1 的**安全构造与"最新值优先"设计是可信的**，吞吐数字（3.76fps / p95 489ms）与代码吻合但被误读为"100ms 采样 + 低延迟"，实际是 `1/(RTT)` 的硬性封顶；**真正需要立刻处理的是两个静默失效**——采集器任一 loop 异常导致永久停帧（B1），以及丢一个 ACK 就重建连接（A3）；而**发布版 FrameMetrics/Perfetto 与 24 小时测试在本次快照中完全没有证据**，这两项应按"未验证"处理，不得计入 0.3.1 验收。

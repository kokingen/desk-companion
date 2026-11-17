# 审查记录与处理（按版本限定）

本文件只记录审查范围与结论，不构成稳定或安全认证。静态审查、合成夹具和辅助检查均不等同硬件验收。0.3.0 的审查只适用 0.3.0。

## 0.3.0（历史，仅适用 0.3.0）

本地 CodeBuddy 实际接收源码完成只读审查及两轮定点复核，没有声称运行动态测试。其最后复核在给定调用顺序与常量时间比较前提下，未发现管理凭据创建/损坏恢复路径中可确认的 P1/P2。这只代表该范围的静态结论。

本地 WorkBuddy 内置 CLI 实际调用后要求登录，未获取可用审查输出。Freebuff 也实际启动了 0.3.0 阶段复查，但较长分析没有及时产出完整报告；不能将其算作最终通过。

2026-11-17 补充本地 CodeBuddy 蓝牙审查：提供 Windows/Android 会话生命周期、取消释放、ACK 流控，以及采集器和加密通道源码。初次提出的“界面静止导致空闲误断”在补充采集器后被其撤回：采样序号随采集持续递增，与画面或 CPU 数值是否变化无关。加密通道的发送锁也已核实。此范围未确认新的 P1/P2；这是静态审查，不能替代硬件回归。

0.3.0 已处理事项包括：管理与手机配对密钥分离；管理凭据受限权限创建、损坏时安全轮换；旧未完成任务不再一直显示工作中；入库前检查传输年龄、校时不确定度、排队时间和字段年龄；Android 旧线程只清理自己的 socket；CPU/内存趋势图分段；通用产品名和设备选择流程；公开构建禁用 PDB 并做白名单导出。

## 0.3.1 Preview

### 审查来源与实际范围

| 来源 | 实际范围 |
|---|---|
| OpenCode | 实际参与实现，并完成历史恢复、持久化围栏、性能与稳定性观察脚本的审查。性能审查为只读静态审查，未运行设备。 |
| Freebuff | 实际执行独立审查/复核，并运行其自行编写的检查器（由父级执行）。结论以父级记录为准，不等同硬件验收。 |
| CodeBuddy / WorkBuddy | 0.3.0 阶段的记录保留；WorkBuddy 本轮仍因登录要求未取得可用输出，不能算 0.3.1 通过。 |

### OpenCode 审查要点

- 历史恢复：v2 hello 提供 `event-id-v1` 能力；手机返回的精确游标与能力选择一起绑定进双方认证上下文。未知/已裁剪 ID 映射为 0；重复 ID 取最早保留出现位置，宁可重放不跳过。唯一残余风险是 ID-only 游标无法区分 PC 日志代际重置；未增加代际标记。
- 持久化围栏：`HistoryGenerationFence` 以常数空间单调拒绝旧代数；持久化失败只围栏待处理的较旧/相同本地连接 epoch，并只关闭匹配 epoch 的 socket，不静默前移检查点。Wi-Fi 路径不携带蓝牙代数/来源字段。
- 性能：`Collector.NetworkLoop` 从样本 START 计时，恢复 20–100ms 采样工作下的约 100ms 节拍；`CompanionPerformance.displayed` 在非录制时跳过 UI 线程 WebView 视觉栅栏，但仍更新 DOM 计数与直方图；CSS `contain` 与头像合成层已静态核对。审查明确指出 `w >= 100ms` 时可能连续补采样，属负载权衡，不是错误数据。
- 稳定性观察脚本：改为读取进程生命周期发送计数器，区分 Agent 会话/PID 重启与真实重连，增加持久化失败增量，并把“观察完成”与“连续有效运行”分开。预检只证明脚本可运行，不是 24 小时验收。

### 已处理的 0.3.1 事项

- 状态通道改为 3 帧有界、最新优先的认证 v2，累计 ACK；v1 保留。
- 网络元数据紧凑化，慢字段不变；速率/间隔量化 0.01 B/s、0.01 ms；保留字段时间戳。
- CPU 250ms、网络 100ms、RAM 500ms 的原生采样路径；RAM 已用值与百分比同源。
- 采样合并门从 START 计时；新增真实每连接与进程生命周期成功加密写入计数器。
- Android 单待处理状态邮箱、独立 IO、真实 DOM 计数。
- 认证历史检查点与持久化失败围栏；不静默推进检查点。
- 正常运行关闭侵入式视觉栅栏；CSS 分离头像与监视画面绘制。
- 配对密钥/证书不变；schema 2 与 6h/30d 历史保留。

### 明确限制

精确最终 .NET 测试程序集已编译，但被本机代码完整性策略拦截，未执行；较早的 73/83 项检查不能替代该程序集。Freebuff 与 OpenCode 的审查均不能替代最终手机/PC 硬件运行。最终手机 70.057 秒仪器化采集已在 USB 重新连接后取回并分析：网络物理年龄 P95/P99（含不确定度）通过 300/500 ms 的两项网络预算，但 CPU/RAM 首次显示 P95 超过 300 ms，严格 10Hz 与稳定 60FPS 未完全达到。最终正常使用 30 分钟 FPS、24 小时、Windows 操作系统重启/睡眠恢复仍未测试。最终生产代码在本文档准备时已冻结，本文件不修改代码。

### 证据与调用记录

- 最终手机分析：[reviews/results/final-phone-instrumented.json](reviews/results/final-phone-instrumented.json)
- 最终 Windows 分析：[reviews/results/final-windows.json](reviews/results/final-windows.json)
- 早期 30 分钟候选：[reviews/results/preliminary-thirty.json](reviews/results/preliminary-thirty.json)
- 测试范围：[reviews/results/testing-scope.json](reviews/results/testing-scope.json)
- 外部子 Agent 实际调用记录：[reviews/INVOCATIONS.json](reviews/INVOCATIONS.json)
- 报告目录：[reviews/Freebuff/](reviews/Freebuff/)、[reviews/OpenCode/](reviews/OpenCode/)

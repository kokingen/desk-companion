# 0.3.1 Preview 交付说明

本文件描述 **0.3.1 Preview** 候选的版本、产物、签名、兼容性与已完成的交付检查。它是预览交付说明，不构成验收或长期稳定性声明。最终生产代码在本文档准备时已冻结，未完成的硬件/长期项标注 NOT TESTED。

## 版本与兼容

| 项目 | 值 |
|---|---|
| 产品名 | Desk Companion |
| 版本 | 0.3.1 Preview |
| Android versionName / versionCode | 0.3.1 / 35 |
| Android applicationId | `net.oxog.phonebox.deskpanel.max3`（保留旧标识以兼容覆盖更新与本地历史） |
| 参考设备 | 小米 Max 3、LineageOS 20 / Android 13、60Hz（仅测试设备，不是产品要求） |
| Windows 平台 | Windows 11 x64，自包含 EXE |
| 数据库 schema | 2（未变更） |
| 历史保留 | 秒级 6 小时、分钟级 30 天（未变更） |
| 配对密钥 / 固定证书 | 未变更，已有配对可继续使用 |
| 协议兼容 | 蓝牙状态通道 v2 与 v1 并存；旧客户端仍走 v1 |

## 构建工具链与依赖锁定

- .NET SDK 10.0.404 由根目录 `global.json` 固定；`Max3.Agent` 与 `Max3.Tests` 使用提交的 `packages.lock.json`，构建脚本执行 `dotnet restore --locked-mode`。
- Node.js 要求 22.12 或更新版本，`package-lock.json` v3 配合 `npm ci`。
- Android 使用 Gradle Wrapper 8.11.1、Android Gradle Plugin 8.7.3、Kotlin 2.0.21；直接库版本固定。Gradle `android/gradle/verification-metadata.xml` 已生成，并启用依赖文件 SHA-256 校验（覆盖已解析依赖，不代表完整供应链可复现）。
- 构建需要 JDK 17 与 Android SDK 35。上述版本约束不等于所有机器的构建环境均已实测。

`applicationId` 与历史源码目录/命名空间保留旧标识，是为了覆盖更新已有安装并保留本地数据；界面、EXE/APK 文件名与文档使用通用名 Desk Companion。

## 产物与完整性

| 产物 | SHA-256 |
|---|---|
| Windows EXE（精确最终） | `768CF093CB34BCB395B902ABF22E153F3DBA30447E548D916A4C39E5DA137508` |
| Android APK（精确最终，已部署） | `B39AA4F68F64919EE08694A210F326F4C0E104B484A11524D972B9C735AEFA77` |

哈希只用于标识本轮产物，不含设备、账户或主机信息。公开包不包含私钥、token、配对数据、数据库、原始日志、个人截图或 Git 历史。

## 签名与运行策略

Windows 预览 EXE **未使用可信发布者签名**。是否允许运行取决于本机代码完整性/智能应用控制等策略；未签名不代表必须购买商业证书，发布者可自行选择签名与分发方式，但本项目不提供绕过系统安全策略的步骤。本轮未更改任何操作系统安全配置。

Android 预览 APK 使用**本地开发签名**，签名私钥不分发。正式发布者应管理自己的发布签名；不同签名通常不能直接覆盖已有安装。本轮精确最终 APK 已在参考设备上覆盖部署并保留原有配对。

精确最终 .NET 测试程序集已编译，但被本机代码完整性策略拦截，**未执行**；较早的 73/83 项原生检查不能替代该程序集。

## 已完成的交付检查

| 检查 | 结果 |
|---|---|
| 精确最终 APK 部署 | 已部署到参考设备并完成恢复测试 |
| 三次返回桌面再进入 | 3.518 / 4.165 / 3.992 秒 |
| 强制停止后恢复 | 3.528 秒 |
| 蓝牙→Wi-Fi 切换及返回 | 实际通过 |
| Agent 进程停止/重启 | 实际通过（**不是**操作系统重启） |
| SQLite quick_check | 正常 |
| 持久化失败计数 | 本轮 0 |
| 配对密钥 / 固定证书 | 未变 |
| 精确最终 Windows 75 秒采样 | 网络 9.887628 Hz、CPU 3.972857 Hz、RAM 1.999760 Hz、成功加密写入 9.842152 Hz；窗口内 0 次新连接、0 次断连检查异常、发送计数器未重置；峰值未完成帧 3 |
| 精确最终手机 70.057 秒仪器化采集 | USB 重新连接后已取回并分析：接收 9.820156 Hz、DOM 9.822316 Hz、physical NEW network 9.531574 Hz；FPS 59.295125、1% Low 29.935753、missed 1.142074%、dropped buffer 0%；FrameMetrics 4151 次、丢 0；battery 35°C（14 样本，USB 连接，电池传感器非 SoC）；traceHealth `[]`、unmatched markers 0 |
| 年龄预算 | 网络物理年龄（含时钟不确定度）P95/P99 279.690625 / 397.009584 ms → 通过 300/500 ms 的**网络两项**预算；CPU/RAM 首次显示 P95 334.41626 / 334.21094 ms，超过 300 ms |

严格 10Hz 与稳定 60FPS 未完全达到。最终仪器化采集包含 visual-fence 插桩开销，不等同正常使用 FPS；最终正常使用 30 分钟 FPS 为 NOT TESTED。

## 未测试 / 未完成

| 项目 | 状态 |
|---|---|
| Windows 操作系统重启 | NOT_TESTED |
| Windows 睡眠/唤醒恢复 | NOT_TESTED |
| 24 小时连续观察 | CANCELLED_BY_USER |
| 最终正常使用 30 分钟 FPS | NOT_TESTED |
| 长期发热、数据库增长、更多 Android 机型 | NOT TESTED |

## 发布与隐私

GitHub 公开发布只使用本轮白名单导出，排除本机配置、证书私钥、配对/管理 token、蓝牙地址、设备序列号、原始日志、数据库、个人截图与完整 Git 历史。公开仓库从脱敏源码快照单独初始化，只保留本次发布的干净初始提交；不会推送上游 PhoneBox 的 Git 历史或开发工作区历史。外层 `PRIVACY-CHECK.json` 记录对文件和归档内部的身份/已知凭据及项目调试记录检查。扫描只覆盖已知模式，不是对所有未知秘密的数学证明。

## 交付文件

- `DeskCompanion-0.3.1-preview.zip`：Windows EXE、Android APK、运行脚本、文档、脱敏实测及外部审查记录。
- `DeskCompanion-0.3.1-source.zip`：完整 Companion 源码快照、构建脚本、锁文件、生成的界面资源和必要许可证，不含完整上游应用的其他产品模块。
- `SHA256SUMS.txt`：两个 ZIP 及实际 EXE/APK 的 SHA-256。
- 每个展开目录中的 `MANIFEST.json`：逐文件相对路径、字节数和 SHA-256。
- 与两个 ZIP 同级另附 `PRIVACY-CHECK.json`：记录本轮已知身份/凭据模式与归档内部扫描结果；扫描范围有限，不构成对未知秘密的数学证明。

0.3.0 程序包和源码包仍保留，最终复核 SHA-256 与备份一致。最终手机已安装版本 0.3.1 / 35，实际安装 APK 与交付 APK 哈希一致；测量已自动停止，ADB 未设置反向转发。

最终 Agent 既有约 103 分钟进程寿命的平均 CPU 0.31282%，低于 2% 目标。详见测量文档和匿名读数。24 小时项按用户明确要求取消，未将短时或既有寿命读数包装成 24 小时通过。

测量方法与预算见 `docs/031-MEASUREMENT.md`；分版本验证状态见 `docs/VALIDATION.md`。

## 证据与调用记录

- [reviews/results/final-phone-instrumented.json](reviews/results/final-phone-instrumented.json)
- [reviews/results/final-windows.json](reviews/results/final-windows.json)
- [reviews/results/preliminary-thirty.json](reviews/results/preliminary-thirty.json)
- [reviews/results/testing-scope.json](reviews/results/testing-scope.json)
- [reviews/INVOCATIONS.json](reviews/INVOCATIONS.json)

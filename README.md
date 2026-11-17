# Desk Companion

**0.3.1 Preview · Windows + Android · 蓝牙主用、Wi-Fi 备用。** 当前为预览版本，已知的性能差距和未测试项在下文及验证记录中公开，不宣称全部验收通过。

## 面向桌面陪伴场景的优势

本项目基于已有开源基础，侧重把闲置 Android 设备作为本地机器人终端。相对所引用项目，主要增加或整合了以下能力；这些是功能与设计取向的差异，没有进行跨项目的同条件性能排名：

| 对照来源 | 本项目的增加与侧重 |
|---|---|
| PhoneBox（工程基础） | 在其桌面面板基础上聚焦本地机器人、可证实的 Codex 任务状态、认证蓝牙优先和私网 Wi-Fi 备用；提供最新值优先的遥测通道与字段新鲜度处理。PhoneBox 原项目提供更多通用组件与跨平台功能，本项目当前支持范围更窄。 |
| Bible Strong Avatar core/web（动画运行库） | 将本地动画与真实任务状态、触摸、监控和 SQLite 历史接到同一个独立终端；动画能力来自该运行库，不将其宣称为本项目原创引擎。 |
| XStat、PcHWmonitor（技术参考） | 本项目侧重无需 USB 常连的陪伴界面、蓝牙主用/无线网备用、采样到显示各阶段的真实测量和明确的过期规则；不以 GPU、温度或传感器覆盖数量作为优势。未复制其 AGPL 实现。 |

各来源、版本、复用范围与许可证单独列于 [引用与来源](docs/SOURCES.md)，完整许可证见 [第三方说明](THIRD_PARTY_NOTICES.md)。

将 Android 手机或平板变成独立运行的桌面机器人终端，显示 Windows 实时指标和可确认的 AI 任务状态。设备本地运行动画、触摸互动、配置及 SQLite 历史；蓝牙主用、Wi-Fi 备用，不依赖投屏、USB、Root 或云端账号。

当前文档对应 **0.3.1 Preview** 候选，尚在收尾验证，不构成完成或验收声明。分版本结果见 `docs/VALIDATION.md`，测量方法与预算见 `docs/031-MEASUREMENT.md`，交付与签名见 `docs/031-DELIVERY.md`。未实际执行的项明确标注 NOT TESTED，不写入推测数值。

## 0.3.1 Preview 主要变化

- 蓝牙状态通道改为 3 帧有界、最新优先的认证 v2 通道并使用累计 ACK；保留 v1 兼容路径。
- 网络元数据更紧凑，慢字段保持不变；速率/间隔线上量化为 0.01 B/s 与 0.01 ms，保留字段原始时间戳。
- CPU 250ms（`GetSystemTimes`）、网络 100ms（`GetIfEntry2`）、内存 500ms（`GlobalMemoryStatusEx`，已用值与百分比取自同一次调用；界面只显示如 `RAM 5.2 GiB · 67%`）。
- 采样合并节拍门从样本 START 计时；网络新样本约 10Hz、CPU 约 4Hz、RAM 约 2Hz，互不将旧数据重新贴成新样本。
- 增加真实“每连接”与“进程生命周期”成功加密状态写入计数器；旧的首尾相减发送率作废，不再作为实际值引用。
- Android 使用单待处理状态邮箱、独立 IO 与真实 DOM 计数。
- 历史断点仅取认证蓝牙提交的最后事件 ID；未知/已裁剪 ID 映射为 0；持久化失败只围栏待处理的较旧或相同本地连接 epoch 并只关闭该 socket，不静默前移检查点。
- 正常使用跳过侵入式 WebView 视觉栅栏（仅显式测量时启用）；CSS 分离头像与监视画面的绘制。
- 现有配对密钥与固定证书不变；数据库 schema 仍为 2，秒级 6 小时与分钟级 30 天历史保留。

## 目前功能

- 机器人本地动画及触摸互动，目标 60 FPS。
- CPU、内存、Ethernet、Wi-Fi 指标，主界面包含最近 60 秒 CPU/内存折线图；绿色 CPU、蓝色内存、固定 0–100% 纵轴，断线/重启/无效数据不连线。
- 本地历史：秒级 6 小时、分钟级 30 天，任务事件有保留上限。过期数据留空，不冒充当前采样。
- Codex 本机桌面任务元数据适配；不读取或发送消息正文、提示词、命令参数。陈旧或无法证实的任务显示未知。CodeBuddy/WorkBuddy 状态尚未接入。
- 已配对设备上的双向密钥认证和加密蓝牙；固定证书/密钥的私网 HTTPS 备用。无远程命令或结束进程接口。

## 支持范围与命名

电脑端目前支持 Windows 11 x64；Android minSdk 28、targetSdk 35，需要可用的现代 WebView。蓝牙需系统支持 RFCOMM；多个已配对设备时在电脑管理页选择设备。Wi-Fi 需两端处于可互访的私网。

参考真机为小米 Max 3、LineageOS 20 / Android 13、60Hz。这个型号只是测试设备，不是产品要求，尚未据此宣称所有 Android 设备均已验收。历史源码目录/命名空间及 Android applicationId 保留旧标识以兼容已有安装和本地历史；产品名、界面、EXE/APK 文件名均使用 Desk Companion。

前台桌面终端使用时保持亮屏，退到后台会暂停；当前不承诺锁屏/后台常驻。

## 运行预览包

1. 解压到固定位置，运行 Start-Companion.ps1 -OpenPairing。电脑管理 API 需要当前用户本地管理凭据，直接打开普通 URL 不会获得管理权限。
2. 手机安装 android/DeskCompanion.apk；已有兼容安装应覆盖更新，卸载会删除本应用配对与历史。
3. 手机点配对扫码/粘贴管理页的一次性链接，核对校验码和完整证书指纹。配对码有效 5 分钟。
4. 在两端系统蓝牙设置中配对，核对数字并授予附近设备权限；手机回到机器人界面，在管理页选择已配对 Android 设备。蓝牙断开时自动尝试 Wi-Fi。

Open-Companion.ps1 打开带当前用户管理授权的本机页面；Stop-Companion.ps1 只停止本目录 Agent。Enable-Startup.ps1 是可选的当前用户登录启动配置，不会自动运行。

## 签名、验证与已知未完成项

Windows 预览 EXE 未使用可信发布者签名，是否获准运行由本机代码完整性/智能应用控制等策略决定。未签名不等于必须购买证书：可选择自签名或由发布者自行决定分发方式，但项目不提供绕过系统安全策略的步骤。Android 预览 APK 使用本地开发签名，签名私钥不分发；正式发布者应管理自己的签名。

本轮最终 Windows EXE 可直接运行并已用于实际采样；但精确最终 .NET 测试程序集在本机被代码完整性策略拦截，已编译未执行。UI 11 项及浏览器检查、JVM 12 项新鲜度与 7 项历史代数检查通过，均为辅助夹具，不等同硬件验收。较早的 73 项与 83 项原生检查分别在本轮较早阶段通过，其中 83 项早于最终历史上下文字面量对齐。

精确最终 70 秒采集已由用户重新连接 USB 后取回并分析：Windows 窗口 74.882 秒、手机仪器化采集 70.057 秒、Perfetto 约 73 秒。实际物理 FPS 59.2951、1% Low 29.9358、missed 刷新机会 1.1421%、dropped buffer 0%；网络物理年龄（含时钟不确定度）P95/P99 为 279.69/397.01 ms，通过 300/500 ms 的**网络两项**预算。CPU/RAM 首次显示 P95 约 334.4/334.2 ms，未达 300 ms；500ms 的 RAM 采样天然可能更旧，不能拿网络 P95 代替全部字段。严格 10Hz 与稳定 60FPS 尚未完全达到。

24 小时长期观察已由用户取消；Windows 操作系统重启/睡眠恢复、最终正常使用 30 分钟 FPS 尚未测试。较早的 30 分钟数字属于另一个候选，单独限定。详见 `docs/VALIDATION.md` 与 `docs/031-MEASUREMENT.md`。脱敏结果在 [docs/reviews/results/final-phone-instrumented.json](docs/reviews/results/final-phone-instrumented.json)、[final-windows.json](docs/reviews/results/final-windows.json)，外部调用记录在 [docs/reviews/INVOCATIONS.json](docs/reviews/INVOCATIONS.json)。

## 从源码构建

安装 .NET SDK 10.0.404（由根目录 global.json 固定）、JDK 17、Android SDK 35、Node.js **22.12 或更新版本**及 npm；在 Windows PowerShell 7 中配置 JAVA_HOME 和 ANDROID_HOME。在 tools/companion-ui 执行 npm ci、npx playwright install chromium，然后运行 tools/build-companion.ps1。NuGet 使用 packages.lock.json 并以 locked-mode 恢复；Node 使用 package-lock.json 与 npm ci；Gradle wrapper 固定为 8.11.1，插件和直接库版本固定。Gradle 提交 `android/gradle/verification-metadata.xml` 的依赖 SHA-256 校验元数据（仅覆盖已解析依赖；不代表整个供应链完全可复现）。

构建脚本会执行 UI 模型检查、浏览器场景、原生检查、Android lint 和 debug/release 构建。额外 JVM 新鲜度检查位于 Android src/test，可通过 tools/test-freshness.ps1 运行。发布构建禁用本项目 PDB 调试信息，避免嵌入开发者本地路径。

Test-Stability.ps1 是辅助链路记录工具，本轮预检只证明脚本可运行，短窗口不代表已通过 24 小时验收，也不能证明手机渲染 FPS 或端到端数据年龄。已知限制和分版本结果见 docs/VALIDATION.md。

## 隐私、许可证与来源

详情见 PRIVACY.md、SECURITY.md 和 THIRD_PARTY_NOTICES.md。公开包不含本机配置、证书私钥、配对/管理 token、蓝牙地址、设备序列号、原始日志、数据库、个人截图或完整 Git 历史。

基于 PhoneBox 0.9.10（MIT），使用 Bible Strong Avatar core/web 1.1.0（MIT）。保留上游版权与许可证；Avatar Studio 不在本项目中。原版 PhoneBox 文档在 docs/UPSTREAM_PHONEBOX.md，不能视为本定制终端具备其全部功能。公开仓库和 Releases 仅提供脱敏源码、程序及实测摘要。

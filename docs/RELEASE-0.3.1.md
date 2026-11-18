# Desk Companion v0.3.1 Preview — 版本说明

> **预览版 / Preview** · Windows 11 x64 + Android 9.0 及以上 · 蓝牙优先，私网 Wi-Fi 备用

Desk Companion 把闲置 Android 手机或平板变成 Windows 电脑旁的**交互式桌面伙伴**：显示 CPU、RAM 与网络指标，结合本地角色动画、触摸互动和可验证的 Codex 任务状态。不需要持续连接 USB，正常使用无需 Root 或云端账户。

> 本版本已发布供试用，但**不代表所有目标性能和长期稳定性测试均已通过**。

## 下载安装 / Downloads

| 文件 | 用途 |
| --- | --- |
| [**DeskCompanion-0.3.1-preview.zip**](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-preview.zip) | Windows Agent、Android APK 和运行脚本；普通用户下载这一项 |
| [DeskCompanion-0.3.1-source.zip](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-source.zip) | 此次版本的可公开源码快照、构建脚本和许可证 |
| [SHA256SUMS.txt](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/SHA256SUMS.txt) | ZIP、EXE、APK 的 SHA-256 校验值 |
| [PRIVACY-CHECK.json](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/PRIVACY-CHECK.json) | 本次公开包的已知敏感信息静态检查摘要 |

[查看 GitHub Release](https://github.com/kokingen/desk-companion/releases/tag/v0.3.1-preview) · [返回项目首页](../README.md)

## 本次更新 / What's new

- **蓝牙优先、Wi-Fi 备用**：认证加密的蓝牙状态通道采用有界队列、最新数据优先与累计 ACK；断开时尝试切换到可互访私网中的 HTTPS 通道。
- **分频原生采样**：Windows 网络目标每 100 ms、CPU 每 250 ms、RAM 每 500 ms 采样；不会将较旧字段伪装成新数据。
- **更直观的监控界面**：RAM 同时展示实际占用和百分比；CPU 与 RAM 提供最近 60 秒趋势图，并区分过期、断连和有效数据。
- **本地互动与历史**：角色动画、触摸反馈、SQLite 历史及断点恢复；本地历史秒级保留 6 小时、分钟级保留 30 天。
- **Codex 状态适配**：只呈现能确认的任务元数据，陈旧任务显示未知；**CodeBuddy / WorkBuddy 状态接入尚未完成**。

## 快速安装 / Quick start

1. 下载并解压 `DeskCompanion-0.3.1-preview.zip` 到固定目录。
2. Windows 端运行 `Start-Companion.ps1 -OpenPairing`，打开本机管理与配对页面。
3. Android 端安装 `android/DeskCompanion.apk`，扫描或粘贴管理页的一次性配对链接，并核对**完整证书指纹**；配对码有效 5 分钟。
4. 在两端系统蓝牙设置中完成配对，授予附近设备权限；在 Windows 管理页选择对应 Android 设备。
5. 连接后优先使用蓝牙；Wi-Fi 备用要求两端网络可互访，不是通过任意公网服务器转发。

完整步骤、兼容性和启动脚本说明见 [README](../README.md#安装与配对)。

## 本次真实测试 / Verified snapshot

参考设备：小米 Max 3、LineageOS 20 / Android 13、约 60 Hz。以下均为**本次精确最终 EXE/APK** 的短时观察数据，不代表所有硬件：

| 指标 | 测量结果 |
| --- | --- |
| Windows 网络 / CPU / RAM 新样本率 | **9.8876 / 3.9729 / 1.9998 Hz**（约 75 秒） |
| Android 真实呈现的新网络状态 | **9.5316 Hz**（约 70 秒仪器化采集） |
| 平均实际帧率 / 1% Low | **59.295 / 29.936 FPS**（含测量插桩） |
| 网络样本到物理呈现年龄 | P95 **279.69 ms**、P99 **397.01 ms**；网络两项预算通过 |
| CPU / RAM 首次显示年龄 P95 | **334.42 / 334.21 ms**；均**未达到** 300 ms 目标 |
| 短时恢复验证 | 蓝牙 ↔ Wi-Fi、Agent **进程**重启和 SQLite quick_check 通过 |

[查看完整验证记录](VALIDATION.md) · [查看测量方法和证据](031-MEASUREMENT.md)

## 已知限制 / Known issues

- **尚未实现严格 10 Hz 的物理呈现更新和稳定 60 FPS**；上述帧率含仪器化测量开销。
- 最终版本**正常使用 30 分钟 FPS、Windows 操作系统重启/睡眠恢复、更多 Android 机型和长期发热**均未完成验收；24 小时观察没有执行。
- 仅支持 **Windows 11 x64** 电脑端；Android 要求 API 28+（Android 9.0+）、现代 WebView 与可用的蓝牙 RFCOMM。手机退到后台会暂停，不承诺锁屏常驻。
- Windows EXE **没有受信任发布者签名**；Android APK 使用本地开发签名。不同 APK 签名通常不能覆盖安装；勿为运行程序而关闭系统安全防护。
- Codex 任务状态仅基于本地可确认的元数据；**不是**消息正文同步、远程控制或通用 AI 客户端。

## 隐私、开源与校验

采集与配对数据按 [隐私说明](../PRIVACY.md) 处理；Windows 私有目录保存本机凭据与配对信息，公开压缩包不包含实际运行密钥、个人日志或数据库。静态扫描只覆盖已知模式，不能证明不存在所有未知敏感信息。

基于 [PhoneBox 0.9.10](https://github.com/ersinkoc/phonebox) 的部分工程代码，并使用 [Bible Strong Avatar Lab](https://github.com/smontlouis/bible-strong-avatar-lab) 的本地动画库；版权和第三方许可详见 [SOURCES](SOURCES.md)、[THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md) 与 [LICENSE](../LICENSE)。未声明对上游项目做过同条件性能比较。

本次 Release 的 ZIP 附件和校验文件保持原样；此文档仅调整产品及版本说明，不替换已发布的二进制内容。

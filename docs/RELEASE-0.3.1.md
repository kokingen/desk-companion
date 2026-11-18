# Desk Companion 0.3.1 Preview

**Preview release · Windows 11 x64 + Android 9.0/API 28+ · Bluetooth primary, private-network Wi-Fi fallback**

Desk Companion turns an Android phone or tablet into an interactive companion for a nearby Windows PC. It displays CPU, RAM and network activity, renders the avatar locally, supports touch interaction, keeps bounded on-device history, and can show verifiable Codex task status.

This is an evaluation release, **not a stable-release certification**. Some performance targets remain unmet and several long-duration/recovery scenarios have not been tested.

## Downloads

| File | Description |
| --- | --- |
| [**DeskCompanion-0.3.1-preview.zip**](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-preview.zip) | Windows Agent, Android APK, launch scripts and user documentation. Most users should download this file. |
| [DeskCompanion-0.3.1-source.zip](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-source.zip) | Sanitized source snapshot, build scripts, dependency locks and license notices. |
| [SHA256SUMS.txt](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/SHA256SUMS.txt) | SHA-256 checksums for the release archives and executable artifacts. |
| [PRIVACY-CHECK.json](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/PRIVACY-CHECK.json) | Summary of the known-pattern privacy scan performed on the public release package. |

[Project homepage](https://github.com/kokingen/desk-companion) · [Full validation record](https://github.com/kokingen/desk-companion/blob/main/docs/VALIDATION.md) · [Measurement methodology](https://github.com/kokingen/desk-companion/blob/main/docs/031-MEASUREMENT.md)

## What's included

- **Wireless PC-to-Android companion:** authenticated, encrypted Bluetooth is the primary state channel; HTTPS over a mutually reachable private network is the fallback.
- **Differential native sampling:** Windows network counters are sampled every 100 ms, CPU every 250 ms, and RAM every 500 ms. Each metric retains its own sample timestamp.
- **Live monitoring UI:** CPU and RAM 60-second trends; RAM displays both used capacity and percentage. Stale and disconnected values are not presented as fresh data.
- **Local avatar and interaction:** the Android app renders the companion locally and supports touch feedback.
- **Bounded local history:** SQLite history retains second-level records for six hours and minute-level records for 30 days.
- **Codex task awareness:** displays selected task metadata only when it can be verified. Stale or ambiguous state is shown as unknown. CodeBuddy and WorkBuddy task-state integration is not implemented in this release.

## Quick start

1. Extract the preview ZIP to a fixed folder on the Windows PC.
2. Run `Start-Companion.ps1 -OpenPairing` to start the Agent and open the local pairing page.
3. Install `android/DeskCompanion.apk` on the Android device.
4. Scan or paste the one-time pairing link. Verify the full certificate fingerprint before accepting; the pairing code expires after five minutes.
5. Pair the devices in the operating-system Bluetooth settings, grant Android Nearby Devices permission, then select the Android device in the Windows management page.
6. Keep both devices on a mutually reachable private network for HTTPS fallback. Do not expose the local management interface to the public internet.

See the [delivery and installation guide](https://github.com/kokingen/desk-companion/blob/main/docs/031-DELIVERY.md) for platform requirements, build details and troubleshooting.

## Validation snapshot

Measurements below refer to the exact 0.3.1 Preview EXE/APK on the reference device (Xiaomi Max 3, LineageOS 20 / Android 13, approximately 60 Hz). This is one short, instrumented test window, not a multi-device benchmark.

| Metric | Observed result |
| --- | ---: |
| Windows network / CPU / RAM sample rates | 9.8876 / 3.9729 / 1.9998 Hz |
| Android physically presented new network updates | 9.5316 Hz |
| Average physical FPS / 1% Low | 59.295 / 29.936 FPS |
| Network sample-to-display age, P95 / P99 (including clock uncertainty) | 279.69 / 397.01 ms |
| CPU / RAM first-display age, P95 | 334.42 / 334.21 ms |

The network-age P95/P99 budgets passed in this measurement. CPU/RAM first-display P95 exceeded the 300 ms target. Strict 10 Hz physical updates and consistently smooth 60 FPS were not achieved. The FPS capture includes measurement instrumentation and is not a normal-use 30-minute result.

Short Bluetooth-to-Wi-Fi switching, return to Bluetooth, Agent **process** restart, and SQLite integrity checks passed. An Agent process restart is not the same as a Windows operating-system reboot.

See [Validation](https://github.com/kokingen/desk-companion/blob/main/docs/VALIDATION.md) and [Measurement notes](https://github.com/kokingen/desk-companion/blob/main/docs/031-MEASUREMENT.md) for scope, definitions and evidence.

## Known limitations

- Strict 10 Hz physical updates and stable 60 FPS goals are not met in the final instrumented capture.
- CPU/RAM first-display P95 exceeded the 300 ms target.
- Final normal-use 30-minute FPS, Windows OS reboot, sleep/wake recovery, 24-hour continuous operation, long-term thermal/storage behaviour and broad Android-device compatibility are **not tested**. The 24-hour run was cancelled and is not a pass.
- The Windows executable is not signed by a trusted publisher. The Android APK uses a local development signing key; an APK signed with a different key may not install as an in-place update. Do not disable operating-system security protections to run this preview.
- The Android companion is a foreground, screen-on experience; lock-screen/background persistence is not promised.
- Codex integration is metadata-only. It does not synchronize prompt/message bodies, execute commands or control the PC. CodeBuddy/WorkBuddy integration is not available.

## Privacy, security and attribution

Metrics and selected task metadata are sent only to the paired, authenticated Android device. Pairing credentials and certificate private keys remain in the Windows user's private-data area; Android history is stored in app-private storage. The Codex adapter does not select prompt text, message bodies or command arguments. Hashed identifiers are pseudonymous, not a guarantee of complete anonymity.

Read [Privacy](https://github.com/kokingen/desk-companion/blob/main/PRIVACY.md) and [Security](https://github.com/kokingen/desk-companion/blob/main/SECURITY.md) before use. The public package was exported from a sanitized source snapshot and scanned for known sensitive patterns; this scan is not a mathematical guarantee against every unknown secret.

Desk Companion reuses selected components from [PhoneBox 0.9.10](https://github.com/ersinkoc/phonebox) and the [Bible Strong Avatar Lab](https://github.com/smontlouis/bible-strong-avatar-lab) avatar runtime. Other projects listed in [Sources](https://github.com/kokingen/desk-companion/blob/main/docs/SOURCES.md) were technical references only. See [third-party notices](https://github.com/kokingen/desk-companion/blob/main/THIRD_PARTY_NOTICES.md) and [LICENSE](https://github.com/kokingen/desk-companion/blob/main/LICENSE) for attribution and license scope. No same-condition performance comparison against upstream projects is claimed.

---

# Desk Companion 0.3.1 Preview（中文版）

**预览版 · Windows 11 x64 + Android 9.0/API 28+ · 蓝牙优先，私网 Wi-Fi 备用**

Desk Companion 将 Android 手机或平板变成 Windows 电脑旁的交互式桌面伙伴：显示 CPU、RAM 和网络活动，在手机本地渲染角色动画，支持触摸互动、有限容量的本地历史记录，并可显示可验证的 Codex 任务状态。

这是供评估使用的预览版，**不是稳定版验收证明**。部分性能目标尚未达到，若干长期运行和系统恢复场景尚未测试。

## 下载

| 文件 | 用途 |
| --- | --- |
| [**DeskCompanion-0.3.1-preview.zip**](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-preview.zip) | Windows Agent、Android APK、启动脚本和用户文档；普通用户下载此文件 |
| [DeskCompanion-0.3.1-source.zip](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-source.zip) | 脱敏源码快照、构建脚本、依赖锁定文件和许可证说明 |
| [SHA256SUMS.txt](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/SHA256SUMS.txt) | 发布压缩包及可执行文件的 SHA-256 校验值 |
| [PRIVACY-CHECK.json](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/PRIVACY-CHECK.json) | 本次公开发布包已知敏感信息扫描摘要 |

## 本次包含的功能

- **Windows 与 Android 无线协作：**认证加密的蓝牙为主要状态通道；两台设备在私网中可互访时，可使用 HTTPS 备用通道。
- **分频原生采样：**Windows 网络每 100 ms、CPU 每 250 ms、RAM 每 500 ms 采样，各指标保留自己的样本时间戳。
- **实时监控界面：**CPU 和 RAM 提供最近 60 秒趋势；RAM 同时显示实际占用量和百分比。过期或断连的数据不会伪装成新数据。
- **本地角色与互动：**Android 端本地渲染角色动画并提供触摸反馈。
- **有限容量的本地历史：**SQLite 秒级数据保留 6 小时，分钟级数据保留 30 天。
- **Codex 任务状态：**仅在能够验证时展示选定的任务元数据；过期或不明确的状态显示为未知。本版本尚未接入 CodeBuddy / WorkBuddy 任务状态。

## 快速开始

1. 将 Preview 压缩包解压到 Windows 电脑的固定目录。
2. 运行 `Start-Companion.ps1 -OpenPairing`，启动 Agent 并打开本地配对页面。
3. 在 Android 设备安装 `android/DeskCompanion.apk`。
4. 扫描或粘贴一次性配对链接；接受前核对完整证书指纹。配对码 5 分钟后失效。
5. 在 Windows 和 Android 的系统蓝牙设置中完成配对，授予 Android「附近的设备」权限，再在 Windows 管理页面选择 Android 设备。
6. 如需 HTTPS 备用通道，两台设备需处于可互访的私有网络。不要将本地管理接口暴露到公网。

## 实测摘要

以下数据来自精确的 0.3.1 Preview EXE/APK，在参考设备（小米 Max 3、LineageOS 20 / Android 13、约 60 Hz）上的短时仪器化采集，不代表多机型基准测试。

| 指标 | 实测结果 |
| --- | ---: |
| Windows 网络 / CPU / RAM 样本率 | 9.8876 / 3.9729 / 1.9998 Hz |
| Android 实际呈现的新网络更新率 | 9.5316 Hz |
| 平均实际 FPS / 1% Low | 59.295 / 29.936 FPS |
| 网络样本到屏幕呈现年龄 P95 / P99（含时钟不确定度） | 279.69 / 397.01 ms |
| CPU / RAM 首次显示年龄 P95 | 334.42 / 334.21 ms |

本次测量中的网络年龄 P95/P99 预算通过；CPU/RAM 首次显示 P95 超过 300 ms 目标。严格 10 Hz 的物理呈现更新和持续稳定 60 FPS 尚未达到。FPS 测量包含插桩开销，不等同于正常使用 30 分钟测试结果。

蓝牙与 Wi-Fi 之间的短时切换、恢复蓝牙、Agent **进程**重启和 SQLite 完整性检查已通过。Agent 进程重启不等于 Windows 操作系统重启。

## 已知限制

- 最终仪器化采集中，严格 10 Hz 物理更新和稳定 60 FPS 目标未达到。
- CPU/RAM 首次显示年龄 P95 超过 300 ms 目标。
- 最终正常使用 30 分钟 FPS、Windows 操作系统重启、睡眠/唤醒恢复、24 小时连续运行、长期温升/存储增长和广泛 Android 机型兼容性均**未测试**。24 小时测试已取消，不能算作通过。
- Windows EXE 未使用可信发布者签名；Android APK 使用本地开发签名。不同签名的 APK 可能无法覆盖安装。不要为了运行预览版而关闭系统安全防护。
- Android 伴侣是前台亮屏体验，不承诺锁屏或后台持续运行。
- Codex 集成仅展示元数据，不同步提示词/消息正文，不执行命令，也不控制电脑；尚未支持 CodeBuddy / WorkBuddy。

## 隐私、安全与来源

指标和选定的任务元数据仅发送给已配对且通过应用认证的 Android 设备。配对凭据与证书私钥保存在 Windows 用户私有目录；Android 历史记录保存在应用私有目录。Codex 适配器不读取提示词、消息正文或命令参数。哈希标识属于化名化，不等于完全匿名。

公开包由脱敏源码快照导出，并对已知敏感模式进行扫描；该扫描不能数学证明不存在所有未知秘密。详情见 [隐私说明](https://github.com/kokingen/desk-companion/blob/main/PRIVACY.md) 与 [安全边界](https://github.com/kokingen/desk-companion/blob/main/SECURITY.md)。

本项目复用了 [PhoneBox 0.9.10](https://github.com/ersinkoc/phonebox) 的部分组件，以及 [Bible Strong Avatar Lab](https://github.com/smontlouis/bible-strong-avatar-lab) 的 Avatar 运行库。其他来源仅作为技术参考。具体归属与许可证范围见 [SOURCES](https://github.com/kokingen/desk-companion/blob/main/docs/SOURCES.md)、[THIRD_PARTY_NOTICES](https://github.com/kokingen/desk-companion/blob/main/THIRD_PARTY_NOTICES.md) 和 [LICENSE](https://github.com/kokingen/desk-companion/blob/main/LICENSE)。本项目没有声称与上游项目进行过同条件性能比较。

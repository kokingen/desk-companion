# 引用与来源（单独标记）

以下链接用于归属、技术参考和测量方法说明。直接复用与仅作参考分开标明，不能将上游功能、性能或许可证归为本项目原创。核对日期：2026-11-17。

## 直接复用的工程与运行库

| 来源与链接 | 使用范围 | 许可证与版本 |
|---|---|---|
| [PhoneBox](https://github.com/ersinkoc/phonebox) · [基线提交](https://github.com/ersinkoc/phonebox/tree/c01dca0f79c76c6675b9f5f99d38f4208ffb5398) | 复用部分 Windows 配对/证书、任务日志代码、Android 传输壳及工程基础；保留上游版权。历史上游说明不代表本定制项目具有其所有功能。 | MIT；基线 0.9.10 |
| [Bible Strong Avatar Lab](https://github.com/smontlouis/bible-strong-avatar-lab) | 仅使用 @bible-strong/avatar-core、@bible-strong/avatar-web 的本地动画运行库及自建角色配置。未引入 Avatar Studio。 | 所用两个运行库均为 MIT，1.1.0；不可将整个上游仓库所有模块概括为同一许可证。 |

其他实际依赖（QRCoder、32feet、AndroidX WebKit、ZXing 等）的锁定版本和许可文本见 [第三方说明](../THIRD_PARTY_NOTICES.md)、源码锁文件及 licenses 目录。

## 仅作技术参考，未打包或复制其实现

- [XStat Hardware Monitoring](https://github.com/Inside4ndroid/XStat-Hardware-Monitoring)：参考 Windows 硬件监控、面板及 Android 终端的产品思路。
- [PcHWmonitor](https://github.com/Xeakaes/PcHWmonitor)：参考 Windows 向 Android 提供硬件指标的使用方式。该项目采用 AGPL-3.0；本项目未复制其 AGPL 实现。

## 测量方法与工具的官方链接

- [Perfetto FrameTimeline](https://perfetto.dev/docs/data-sources/frametimeline)：应用帧与 SurfaceFlinger 显示帧关联。
- [Perfetto Trace Processor](https://perfetto.dev/docs/analysis/trace-processor)：分析已记录的真实 trace；工具不随本项目程序分发。
- [Android FrameMetrics](https://developer.android.com/reference/android/view/FrameMetrics)：帧阶段度量；不以界面写出的 FPS 数字替代真机统计。
- [Android WebView](https://developer.android.com/reference/android/webkit/WebView)：visual-ready 回调不是物理呈现，需另关联显示帧。
- [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)：同次物理内存采样生成 GiB 与百分比。

本项目的区别在于面向本地桌面陪伴场景的整合、认证蓝牙与 Wi-Fi 备用、历史续传和真实测量证据，不表示在全部功能或所有设备上优于上述项目。当前实测与未通过项目见 [测量记录](031-MEASUREMENT.md)。

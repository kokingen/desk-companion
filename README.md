<p align="center">
  <img src="assets/desk-companion-mark.svg" width="112" height="112" alt="Desk Companion logo" />
</p>

<h1 align="center">Desk Companion</h1>
<p align="center"><strong>An animated Android companion for monitoring your Windows PC.</strong></p>
<p align="center">Live CPU · RAM · Network &nbsp;|&nbsp; Bluetooth first · Private Wi-Fi fallback</p>
<p align="center">
  <a href="https://github.com/kokingen/desk-companion/releases/tag/v0.3.1-preview"><strong>Download Preview 0.3.1</strong></a>
  &nbsp;·&nbsp;
  <a href="docs/031-DELIVERY.md">Installation guide</a>
  &nbsp;·&nbsp;
  <a href="docs/VALIDATION.md">Test results</a>
</p>

<p align="center">
  <a href="https://github.com/kokingen/desk-companion/releases"><img src="https://img.shields.io/github/v/release/kokingen/desk-companion?include_prereleases&label=preview" alt="Latest preview" /></a>
  <img src="https://img.shields.io/badge/Windows-11%20x64-blue" alt="Windows 11 x64" />
  <img src="https://img.shields.io/badge/Android-9%2B-3DDC84" alt="Android 9 and later" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT license" /></a>
</p>

Desk Companion pairs a Windows 11 PC with an Android phone or tablet. The Android device renders the companion locally, shows live performance metrics, responds to touch, keeps bounded local history, and can display verifiable Codex task status. Normal use does not require Root, a permanent USB cable, or a cloud account.

> **Preview 0.3.1:** usable for evaluation, but not a fully validated stable release. Strict 10 Hz physical updates and consistently smooth 60 FPS have not been achieved; CPU/RAM first-display latency also misses its target. See [known limitations](#known-limitations) and the [validation record](docs/VALIDATION.md).

## Get started

<blockquote>
**For most users:** download the Windows + Android bundle from the GitHub Release. You do not need the source archive unless you intend to inspect or build the project.
</blockquote>

[**Download Desk Companion 0.3.1 Preview**](https://github.com/kokingen/desk-companion/releases/download/v0.3.1-preview/DeskCompanion-0.3.1-preview.zip) · [All release files and notes](https://github.com/kokingen/desk-companion/releases/tag/v0.3.1-preview)

1. Extract the preview ZIP to a fixed folder on the Windows PC.
2. Run `Start-Companion.ps1 -OpenPairing` from that folder to start the Agent and open its local pairing page.
3. Install `android/DeskCompanion.apk` on the Android device.
4. Scan or paste the one-time pairing link shown by the Windows pairing page. Verify the full certificate fingerprint before accepting; the pairing code expires after five minutes.
5. Pair the devices in Android/Windows Bluetooth settings, grant the Android Nearby Devices permission, then select the paired Android device in the Windows management page.
6. Keep both devices on a mutually reachable private network if you want Wi-Fi fallback. Bluetooth is the preferred transport.

See [the detailed pairing and operation notes](docs/031-DELIVERY.md) before troubleshooting. Do not expose the local management interface to the public internet.

## What it does

- **Live PC monitoring.** Displays CPU, RAM and network activity. The main screen includes a rolling 60-second CPU/RAM chart; RAM shows both used capacity and percentage.
- **An interactive Android companion.** Runs the avatar locally, with touch feedback and a monitoring view. It is not a video stream or screen mirror.
- **Wireless connection.** Uses authenticated, encrypted Bluetooth as the primary state channel and private-network HTTPS as a fallback. A permanent USB cable is not required.
- **Local history.** Stores bounded history in SQLite on the Android device: second-level records for six hours and minute-level records for 30 days.
- **Codex task awareness.** Reads selected local task metadata to show a task state when it can be verified. Stale or ambiguous state is shown as unknown rather than guessed.

### Sampling intervals

The Windows Agent samples different metric classes at different rates:

| Metric | Native sampling interval | Approximate rate |
|---|---:|---:|
| Network counters | 100 ms | 10 Hz |
| CPU | 250 ms | 4 Hz |
| RAM | 500 ms | 2 Hz |

These are **Windows sampling intervals**, not a promise that every value is transmitted or physically displayed at the same rate. Each field retains its own timestamp; an old CPU or RAM sample is not relabelled as new just because a network update arrived.

## How it works

```mermaid
flowchart LR
  subgraph PC["Windows 11 PC"]
    M["CPU / RAM / network sampler"] --> A["Windows Agent"]
    C["Codex task metadata"] --> A
  end
  A -- "Authenticated Bluetooth · primary" --> D["Android companion"]
  A -- "Private-network HTTPS · fallback" --> D
  subgraph Android["Android device"]
    D --> UI["Local avatar + live metrics"]
    D --> H["SQLite history"]
  end
```

The Codex integration is **read-only status awareness**. It reads selected local task metadata and does not synchronize prompt/message bodies, execute commands, control the PC, or terminate tasks. CodeBuddy and WorkBuddy task-state integration is not currently implemented.

## Requirements

| Component | Requirement |
|---|---|
| PC | Windows 11, x64 |
| Android device | Android 9.0 / API 28 or later |
| Android runtime | A reasonably current Android System WebView |
| Bluetooth | OS support for Bluetooth RFCOMM |
| Wi-Fi fallback | Both devices on a mutually reachable private network |

The Xiaomi Max 3 running LineageOS 20 / Android 13 was used as the reference test device. It is **not** a minimum hardware requirement, and the current test results should not be interpreted as validation of every Android model.

The companion is a foreground experience: leaving the app or turning the screen off can pause it. Background/lock-screen persistence is not currently promised.

## Current validation snapshot

The following measurements are from the exact 0.3.1 Preview Windows executable and Android APK, using the reference device. They are short test windows, not a cross-device benchmark.

| Measurement | Observed result |
|---|---:|
| Windows network / CPU / RAM sample rates | 9.8876 / 3.9729 / 1.9998 Hz |
| Android physically presented new network updates | 9.5316 Hz |
| Average physical FPS / 1% Low | 59.295 / 29.936 FPS |
| Network sample-to-display age, P95 / P99 (including clock uncertainty) | 279.69 / 397.01 ms |
| CPU / RAM first-display age, P95 | 334.42 / 334.21 ms |

The network age met its two stated budgets in this measurement. CPU/RAM first-display P95 exceeded the 300 ms target. Strict 10 Hz physical updates and consistently smooth 60 FPS have **not** been achieved. The FPS capture included instrumentation overhead and is not a normal-use 30-minute result.

Short Bluetooth-to-Wi-Fi switching, return to Bluetooth, Agent process restart and SQLite integrity checks passed in the documented test. These checks do not establish Windows reboot or sleep/wake recovery.

For methodology, evidence, per-version scope and untested items, read [Validation](docs/VALIDATION.md) and [Measurement notes](docs/031-MEASUREMENT.md). Do not infer a pass from a test that is marked NOT TESTED.

## Known limitations

- The 0.3.1 Preview does not yet meet the strict 10 Hz physical-update or stable 60 FPS goals.
- CPU/RAM first-display age P95 exceeded the 300 ms target in the final instrumented capture.
- Normal-use 30-minute FPS, Windows OS reboot, Windows sleep/wake, 24-hour continuous operation, long-term thermal/storage behaviour and broad Android-device compatibility have not been validated. The 24-hour run was cancelled and is not a pass.
- The Windows executable is not signed by a trusted publisher. The Android APK uses a local development signing key; a differently signed APK may not install as an in-place update. Do not disable OS security protections to run the preview.
- Codex status is metadata-only and may be unknown when a task cannot be verified. CodeBuddy/WorkBuddy integration is not available yet.

## Build from source

The repository includes the source snapshot, build scripts and dependency lock/verification files. The documented build environment uses:

- .NET SDK 10.0.404 (pinned by `global.json`)
- JDK 17 and Android SDK 35
- Gradle Wrapper 8.11.1
- Node.js 22.12 or later and npm
- Windows PowerShell 7

Configure `JAVA_HOME` and `ANDROID_HOME`, then follow the build steps in [the delivery document](docs/031-DELIVERY.md). The build uses locked NuGet restore and `npm ci`; Gradle dependency verification covers the resolved dependencies recorded in its verification metadata. This is not a claim that every part of the software supply chain is fully reproducible.

## Privacy and security

- Metrics and task status are intended for the paired, authenticated Android device.
- The Codex adapter does not send prompt text, message bodies or command arguments.
- Pairing material, local management credentials and the certificate private key remain in the Windows user's private-data area. Android history and pairing data are stored in app-private storage.
- Normal use does not require Root or a cloud account.
- Do not publish the Windows private-data directory, pairing links/codes, Android app database or diagnostic logs.

Read [Privacy](PRIVACY.md) and [Security](SECURITY.md) before using the preview on a network you do not control.

## Project status and licensing

This project builds on selected engineering components from [PhoneBox 0.9.10](https://github.com/ersinkoc/phonebox) and uses the [Bible Strong Avatar Lab](https://github.com/smontlouis/bible-strong-avatar-lab) avatar-core/avatar-web runtime. [XStat](https://github.com/Inside4ndroid/XStat-Hardware-Monitoring) and [PcHWmonitor](https://github.com/Xeakaes/PcHWmonitor) were technical references only; their implementations are not included.

Reuse scope, upstream versions, attribution and third-party license details are documented in [Sources](docs/SOURCES.md) and [Third-party notices](THIRD_PARTY_NOTICES.md). The repository includes the MIT license and retains applicable upstream notices. No claim is made that Desk Companion contains every feature of the upstream projects or outperforms them in a same-condition benchmark.

## Project links

- [Releases and downloads](https://github.com/kokingen/desk-companion/releases)
- [0.3.1 Preview release notes](docs/RELEASE-0.3.1.md)
- [Validation record](docs/VALIDATION.md)
- [Measurement methodology](docs/031-MEASUREMENT.md)
- [Delivery and build details](docs/031-DELIVERY.md)
- [Sources and attribution](docs/SOURCES.md)
- [Privacy](PRIVACY.md)
- [Security policy](SECURITY.md)

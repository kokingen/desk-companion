# 安全边界与已知限制

本文件描述 **0.3.1 Preview** 候选的安全边界，不构成安全认证或长期稳定性保证。分版本验证状态见 `docs/VALIDATION.md`。

## 认证与传输

管理 HTTP 仅绑定回环地址，校验 Host/Origin；除不含凭据的静态首页外，管理 API 要求独立随机管理 token。Open-Companion.ps1 从当前用户受限目录读取 token，通过 URL fragment 交给页面，页面立即清除 fragment，后续只用 Authorization 头。token 不放入查询参数、配对链接或日志。

手机 HTTPS 仅绑定明确的私网 IPv4 与回环，使用固定证书指纹和独立配对密钥。一次性配对码过期且限流，手机需核对校验码。蓝牙要求 OS 配对，在此基础上双向 HMAC 证明同一配对密钥，使用方向分离 AES-GCM、随机握手 nonce、严格帧序号和长度限制。设备名称本身不作为身份凭据。

0.3.1 的状态通道为有界最新优先 v2 并保留 v1；历史恢复能力与精确游标绑定进双方认证上下文，未知/已裁剪事件 ID 映射为 0。Android 仅把“认证蓝牙提交的最后事件 ID”作为检查点，并与对应事件写入同一事务；持久化失败只围栏待处理的较旧或相同本地连接 epoch，并只关闭该连接自己的 socket，不静默前移检查点。Wi-Fi 路径不接受蓝牙来源字段。

## 不在隔离边界内

同一 Windows 用户下的恶意程序、管理员/SYSTEM、已被攻陷或 Root 后的手机不在该隔离边界内。屏幕上的配对链接、管理 token、配对密钥、私钥或数据库泄露会破坏相应保障。不要分享这些内容。

本机私有 SQLite 设置被本地篡改可造成重放或尝试提前游标，但在没有现有配对密钥的情况下不能伪造认证握手上下文。PC 的 hello 尚未纳入认证转录；能主动改写 hello 的攻击者可抑制历史恢复能力，形成仅影响带宽的降级，不能使 PC 跳过历史。

## 系统与签名

无远程命令、结束进程或公网管理接口。不自动修改防火墙、Defender、系统应用控制、ROM 或固件。未签名 Windows EXE 可能被系统拦截；项目不提供绕过步骤，也不要求用户购买证书。Android 预览包的开发签名不等同正式发布身份认证。

本轮未更改任何操作系统安全配置。最终 Windows EXE 为未签名构建，已实际运行；但精确最终 .NET 测试程序集在本机被代码完整性策略拦截，属于“已编译、未执行”，不能算作该程序集的测试通过。

## 长期与未测试项

Codex 桌面数据库格式属于外部实现，未来可能变化；读取失败时显示未知。不同手机厂商的蓝牙/省电策略尚未全面覆盖。

精确最终 70 秒仪器化采集已取回并分析，但以下仍未完成，不能据此声称安全或长期稳定：24 小时连续观察（用户已取消）、Windows 操作系统重启、睡眠/唤醒恢复、最终正常使用 30 分钟 FPS、更多 Android 机型与长期发热/数据库增长。仪器化采集包含插桩开销，不等同正常使用 FPS。最终短窗口只有**网络物理年龄** P95/P99 通过 300/500 ms 预算；CPU/RAM 首次显示 P95 超过 300 ms，不能把网络结果外推到所有字段。当前仅承诺前台亮屏桌面终端使用，不承诺锁屏/后台常驻。CodeBuddy/WorkBuddy 的任务状态尚未接入。

脱敏测量结果见 [docs/reviews/results/final-phone-instrumented.json](docs/reviews/results/final-phone-instrumented.json) 与 [docs/reviews/results/final-windows.json](docs/reviews/results/final-windows.json)；外部调用记录见 [docs/reviews/INVOCATIONS.json](docs/reviews/INVOCATIONS.json)。

## 报告问题

只提供脱敏复现步骤、版本和必要错误类型；先移除 token、私钥、序列号、主机名、地址和任务正文；不要上传完整运行目录。

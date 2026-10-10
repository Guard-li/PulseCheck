# 安全设计

## 默认安全边界

1. Agent 仅监听局域网接口，默认不开放公网端口。
2. 所有敏感 API 使用 HTTPS；首次访问自签名证书时必须由用户核对 SHA256 指纹。
3. 配对码为一次性 6 位数字，默认 15 分钟失效。
4. 配对成功后返回长期随机 Token；服务端仅保存 SHA256 哈希，不保存明文 Token。
5. 所有读取 API 需要 Bearer Token；写入、扫描、调度和撤销设备需要 `owner` 角色。
6. Token 不写入普通日志；结构化日志对包含 Token 字段的消息进行省略处理。
7. 报告默认隐藏 MAC、IP、用户名和序列号；JSON、HTML、CSV、PDF 均支持脱敏。
8. 所有本地数据默认不上传、不开启遥测。
9. 优化动作默认只给出建议，不自动结束进程、删除服务、修改注册表或关闭 Defender。

## 配对流程

```text
Agent 启动
  → 生成一次性配对码，保存哈希和过期时间
  → 本机 GET /api/v1/pairing/info 显示配对码、二维码和 TLS 指纹
  → 控制器 POST /api/v1/auth/pair
  → 验证配对码并生成随机 Token
  → 控制器保存 Token 并固定证书 SHA256
  → 后续 API / WebSocket 使用 Token
```

## 传输与证书

- Kestrel 生成 2048 位 RSA 自签名证书，包含主机名、localhost 和活动局域网地址。
- 证书与私钥保存在 `data/agent-tls.pfx`，文件设置为隐藏；安装脚本应进一步限制 ACL。
- 控制器在 `badCertificateCallback` 中比较证书 DER 的 SHA256；指纹不匹配时拒绝连接。
- 远程访问建议使用用户自建 WireGuard、Tailscale 或 ZeroTier，不要把 17821 端口直接暴露到公网。

## 权限模型

- `readonly`：读取指标、历史、报告和告警。
- `owner`：额外允许创建/取消扫描、管理调度、撤销其他控制器。

## 服务账户建议

安装脚本默认使用 LocalSystem，以便读取完整系统信息。若安全策略不允许，可手动修改服务账户，但必须接受部分传感器、SMART 和事件日志不可用。不要为“完整功能”绕过 Windows 安全控制。

## 漏洞报告

请在私有渠道报告漏洞，并包含：

- 影响版本和 Windows 版本
- 复现步骤
- 是否需要配对权限
- 是否涉及 Token、证书、文件路径或提权
- 已脱敏日志和证据

不要提交真实 Token、PFX、系统序列号或用户隐私数据。
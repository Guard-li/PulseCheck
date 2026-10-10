# 故障排查

## 浏览器提示证书不受信任

这是首次使用自签名证书的预期行为。请先通过本机 `GET /api/v1/pairing/info` 或安装日志核对 SHA256 指纹，再在浏览器中信任该证书。不要无条件信任未知局域网地址的证书。

## 服务启动后无法访问 17821

```powershell
Get-Service PulseCheckAgent
sc.exe query PulseCheckAgent
Get-NetTCPConnection -LocalPort 17821 -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayName 'PulseCheck Agent (Private)'
```

检查私有网络配置文件和 Windows 防火墙。手机热点/USB 网络共享可能被 Windows 标记为“公用网络”，需要用户明确允许。

## 没有 CPU 温度或功耗

Windows 标准 API 不总是提供稳定温度。PulseCheck 不会用估算值替代。需要用户自行安装兼容传感器驱动，或将 CPU 温度和功率保持为不可用。

## GPU 只有名称，没有利用率

Intel/AMD 实时利用率尚未接入厂商 API；NVIDIA 需要 `nvidia-smi` 在 PATH 中可用。运行：

```powershell
nvidia-smi --query-gpu=name,utilization.gpu --format=csv,noheader,nounits
```

## SMART 为空

以管理员身份运行服务，并确认 Windows Storage 管理组件可用：

```powershell
Get-PhysicalDisk | Get-StorageReliabilityCounter
```

不同驱动和 USB 桥接器可能拒绝暴露 SMART。

## 报告没有 PDF 或 PDF 中文异常

优先使用 HTML；或将 Microsoft Edge 安装到默认路径，并确认无头模式可运行：

```powershell
msedge.exe --headless=new --disable-gpu --print-to-pdf="$env:TEMP\test.pdf" "about:blank"
```

## SQLite 报错

确认数据目录可写，并检查是否有足够的磁盘空间。数据库使用 WAL 模式，复制或备份时应同时保留 `pulsecheck.db`、`pulsecheck.db-wal` 和 `pulsecheck.db-shm`，或先停止服务。

## 配对码无效

配对码默认 15 分钟一次性有效。重新启动 Agent 会轮换配对码。通过本机 API 获取新码：

```powershell
Invoke-RestMethod -SkipCertificateCheck https://localhost:17821/api/v1/pairing/info
```

## 日志位置

```text
%ProgramData%\PulseCheck\data\logs\pulsecheck-YYYYMMDD.jsonl
```

日志按天和 10MB 轮转。不要提交含 Token 或用户隐私的日志。
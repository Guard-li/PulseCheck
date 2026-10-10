# PulseCheck 架构

## 1. 设计原则

- **本地优先**：采集、存储、诊断和报告默认在 Agent 所在电脑完成。
- **真实数据**：读取失败时返回 `Unavailable`、权限不足或驱动缺失，不生成模拟值。
- **边界诚实**：无 Agent 不能读取远程硬件传感器；所有限制在 API、报告和 UI 中明确展示。
- **分层隔离**：采集器、诊断规则、持久化、报告、网络 API 和 UI 分离，核心逻辑可测试。
- **安全默认**：局域网 HTTPS、一次性配对码、长期 Token、默认脱敏、默认不访问公网。
- **可替换**：产品名、端口、路径、采样间隔和规则均来自配置。

## 2. 进程与数据流

```text
Windows 硬件 / WMI / 注册表 / PDH / nvidia-smi / .NET API
                       │
                       ▼
              Agent 采集器（Collectors）
                       │ MetricSnapshot
        ┌──────────────┼────────────────┐
        ▼              ▼                ▼
   SQLite 历史      WebSocket        诊断引擎
        │              │                │
        ▼              ▼                ▼
   历史 API       控制器实时面板     HealthScore / Findings
                                         │
                                         ▼
                                  HTML/JSON/CSV/PDF 报告
```

Controller 只负责连接、授权、展示和控制，不承担远程硬件传感职责。Agent 离线时，历史数据保留在本地 SQLite，重新连接后可继续读取。

## 3. 项目分层

- `PulseCheck.Contracts`：API 请求/响应、指标、报告、扫描、告警、调度模型；不依赖 UI。
- `PulseCheck.Agent`：系统采集、SQLite、诊断、报告、调度、安全、发现服务。
- `PulseCheck.AgentService`：Kestrel HTTPS、认证、REST、WebSocket、静态 Web 控制台、Windows Service 生命周期。
- `PulseCheck.Tests`：核心逻辑测试；因目标机器没有预装 xUnit，使用无外部包的测试运行器，避免依赖下载影响离线开发。
- `controller-flutter`：面向 Android/Windows 的 Flutter 客户端源码。

## 4. 采集策略

- 普通采样默认 1 秒，范围 0.5 到 10 秒；采样循环只写必要数据并按时清理。
- CPU 使用 `NtQuerySystemInformation` 读取每核心时间，频率使用 `CallNtPowerInformation`。
- 内存使用 `GlobalMemoryStatusEx` 和 `GetPerformanceInfo`，SMBIOS 仅读取真实插槽信息。
- 磁盘卷使用 `DriveInfo`，吞吐/响应使用英文 PDH 计数器；SMART 使用 Windows Storage PowerShell 接口，管理员模式下才尝试。
- 网络使用 `NetworkInterface` 统计和系统 TCP 连接；Wi-Fi 细节通过 `netsh wlan` 解析，失败时留空。
- NVIDIA GPU 优先使用 `nvidia-smi`；Intel/AMD 当前提供静态适配器与驱动信息，实时 GPU 利用率标记为实验性限制。
- 传感器通过标准 ACPI 尝试读取温度；无稳定 API 或驱动时明确标记为需要传感器驱动。

## 5. 诊断与评分

评分维度及权重由 `config/health-rules.json` 提供：CPU 15%、内存 15%、存储 20%、温度/散热 15%、稳定性 15%、网络 10%、安全更新 10%。每条规则包含 ID、指标路径、比较符、阈值、连续采样数、严重级别、扣分、证据、原因和建议。诊断引擎只基于指标快照执行规则；报告保存原始快照、证据字段、规则版本和生成时间。

## 6. 存储

SQLite 表包含：

- `metrics`：指标快照 JSON 和 UTC 时间
- `scans`：扫描生命周期与进度
- `reports`：报告摘要和完整报告 JSON
- `schedules`：Cron、条件和下次执行时间
- `alerts`：规则告警、去重键和恢复状态

数据库使用 WAL、`busy_timeout` 和单连接事务写入。原始指标默认保留 7 天，聚合/报告策略在后续版本扩展。

## 7. Windows Service

`WindowsServiceRuntime` 通过 `advapi32` 直接调用 Service Control Manager，避免引入外部托管包。服务启动后并行启动：

- 指标采样后台服务
- UDP 设备发现服务
- Cron 调度服务
- Kestrel HTTPS API

服务重启后，SQLite 中的任务和身份会恢复。`sc.exe` 安装脚本配置失败重启和延迟自动启动。

## 8. 当前技术选择说明

- 使用 **.NET 8 LTS** 目标框架；开发机使用 .NET 9 SDK 构建，并在 `Directory.Build.props` 中允许开发环境 Major roll-forward，发布包为自包含运行，不依赖目标机运行时。
- 不使用 EF Core 或 SQLite 包，避免离线环境 NuGet 依赖；直接调用 Windows `winsqlite3`。
- 不使用 LibreHardwareMonitor 内核驱动；不自动安装来源不明驱动。
- PDF 优先通过 Microsoft Edge 无头打印，缺失时生成基础 PDF并在报告中记录警告。
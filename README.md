# PulseCheck

PulseCheck 是一个本地优先、跨设备、可扩展的电脑健康检测与实时监控平台。第一版包含可运行的 Windows Agent 服务、HTTPS REST API、WebSocket 实时数据、SQLite 历史库、配置化诊断评分、HTML/JSON/CSV/PDF 报告、定时任务、基础告警、UDP 局域网发现、内置 Web 控制台和 Flutter 控制器源码。

> 当前状态：**首轮可运行版本 / MVP 基础版**。真实硬件、系统、性能与报告链路已实现；Flutter 工程因开发机未安装 Flutter SDK，尚未在本机产出 APK/Windows 包。详见[当前完成度](#当前完成度)和[已知限制](#已知限制)。

## 功能概览

- **Windows Agent**：.NET 8 LTS 自包含服务，支持控制台运行、Windows Service 安装和便携运行。
- **真实采集**：CPU 总/每核心利用率、频率、内存、卷、进程、磁盘吞吐/响应、网络速率、NVIDIA GPU、系统、电池、安全状态等。
- **本地存储**：Windows 自带 `winsqlite3`，无需外部 SQLite 包；原始指标默认保留 7 天。
- **实时数据**：默认每 1 秒采样，通过 `/api/v1/realtime` WebSocket 推送。
- **安全配对**：一次性 6 位配对码、长期随机 Token、SHA256 Token 哈希、自签名 TLS、证书指纹、只读/所有者角色。
- **体检与评分**：快速/完整模式，规则写在 `config/health-rules.json`，结果包含证据、原因、建议和管理员要求。
- **报告**：HTML、JSON、CSV、PDF；默认脱敏 MAC、IP、用户名和序列号；报告和每个文件均有 SHA256。
- **调度与告警**：持久化 Cron 任务、错过补偿、最大运行时长、基础告警去重和恢复状态。
- **控制器**：内置响应式 Web 控制台；Flutter Android/Windows 共用源码位于 `src/controller-flutter`。
- **可交付**：Release 构建、冒烟测试、自包含发布、SHA256、安装/升级/卸载脚本和 GitHub Actions。

## 快速运行

### 环境

- Windows 10 22H2 / Windows 11 x64
- .NET 8 SDK 或更高版本 SDK（开发）
- Windows 自带 `winsqlite3.dll`
- 浏览器访问控制台时可忽略自签名证书警告，但应先核对设置页/配对信息中的 SHA256 指纹

```powershell
dotnet restore PulseCheck.sln
dotnet build PulseCheck.sln -c Debug
dotnet run --project src/agent-service/PulseCheck.AgentService.csproj
```

浏览器打开：

```text
https://localhost:17821
```

首次启动会生成：

```text
src/agent-service/bin/Debug/net8.0-windows/data/
  agent-identity.json
  agent-tls.pfx
  security.json
  pulsecheck.db
  logs/
```

本机 `GET /api/v1/pairing/info` 会返回一次性配对码和二维码 SVG；Web 控制台在本机打开时会自动填入配对码。远程设备应通过受信任方式获取配对码，并核对 TLS 指纹。

## 构建、测试和发布

```powershell
# 开发构建 + 自动化测试 + JS 语法检查；若安装 Flutter，则同时执行 Flutter 分析/测试
.\scripts\test.ps1

# Release + 自包含 win-x64 发布 + SHA256SUMS.txt
.\scripts\build.ps1

# 对发布目录执行真实冒烟测试（启动、配对、指标、快速体检、报告）
.\scripts\smoke-test.ps1
```

发布结果默认位于 `artifacts/win-x64`，包含 `PulseCheck.AgentService.exe`、配置、健康规则、Web 控制台、运行库和 `SHA256SUMS.txt`。如需 ZIP 交付包：

```powershell
.\scripts\package.ps1
```

## Windows 服务安装

```powershell
# 先以管理员 PowerShell 运行
.\scripts\build.ps1
.\installer\windows\install-agent.ps1
```

默认路径：

- 程序：`C:\Program Files\PulseCheck\Agent`
- 数据：`C:\ProgramData\PulseCheck\data`
- 报告：`C:\ProgramData\PulseCheck\reports`
- 服务：`PulseCheckAgent`
- API：`https://<本机地址>:17821`
- UDP 发现：`17820`

升级前会备份数据：

```powershell
.\installer\windows\upgrade-agent.ps1
```

卸载默认删除程序、服务和防火墙规则，可保留数据：

```powershell
.\installer\windows\uninstall-agent.ps1 -KeepData
```

## 控制器构建

### Web 控制台

Agent 已内置响应式 Web 控制台，无需 Node 构建，直接访问 Agent 的 HTTPS 地址即可。

### Flutter Android / Windows

```powershell
cd src/controller-flutter
flutter pub get
flutter analyze
flutter test
flutter build apk --release
flutter build windows --release
```

如果目录中缺少 `android/` 或 `windows/` 平台工程，可在 Flutter SDK 安装后执行：

```powershell
flutter create --platforms=android,windows .
```

## 常用 API

- `GET /api/v1/health`：无需认证，仅返回服务存活信息
- `GET /api/v1/pairing/info`：仅允许回环地址，返回配对码、二维码和证书指纹
- `POST /api/v1/auth/pair`：一次性配对码换取 Token
- `GET /api/v1/metrics/current`：当前真实快照
- `GET /api/v1/metrics/history`：时间范围历史数据
- `POST /api/v1/scans`：创建快速或完整体检
- `GET /api/v1/reports`：报告列表
- `GET /api/v1/reports/{id}/artifacts/{format}`：下载 JSON/HTML/CSV/PDF
- `/api/v1/realtime`：WebSocket，子协议 `pulsecheck.v1` + `token.<Token>`

完整说明见 [docs/API.md](docs/API.md)。

## 当前完成度

### 已完成并验证

- .NET 8 LTS Release 构建：0 警告、0 错误
- 核心测试：7/7 通过
- HTTPS 自签名证书与指纹
- 配对码、Token、只读/所有者权限
- CPU/内存/磁盘/网络/GPU/进程/系统真实采集
- Windows SQLite 持久化和历史查询
- WebSocket 实时推送
- 快速体检、规则评分、证据链
- HTML/JSON/CSV/PDF 真实报告下载
- 报告默认脱敏与 SHA256
- Cron 调度和任务持久化
- 内置 Web 控制台
- Flutter 控制器源码和单元测试

### 实验性或计划中

- Flutter Windows/Android 安装包：本机未安装 Flutter SDK，尚未编译
- iOS：第二阶段
- mDNS：当前使用 `PulseCheck` UDP 发现协议；文档中不宣称已实现 mDNS
- Intel/AMD GPU 实时利用率：当前只读取适配器和驱动信息；NVIDIA 使用 `nvidia-smi`
- CPU 温度/功耗：Windows 标准 API 不稳定；无传感器驱动时明确返回不可用
- 完整检测的压力/基准测试：当前采集真实慢速检查，不执行未经确认的磁盘写入测试
- 自建 VPN/中继、邮件、移动推送、AI 分析：后续阶段

## 已知限制

1. 无 Agent 时只能做主机、端口和网络可达性检测，不能读取远程 CPU、温度、SMART 或进程。
2. 完整模式需要管理员权限；受限模式不会崩溃，但会显示权限缺失证据。
3. 自签名证书需要控制器首次信任或按指纹固定。
4. Windows 服务默认以 LocalSystem 运行；如需最小权限部署，应在 `SECURITY.md` 指南下评估。
5. 磁盘 SMART、Wi-Fi 细节、服务状态和事件日志依赖 Windows 版本、驱动和权限，读取失败时返回“不可用”而不是伪造值。
6. Web 控制台当前是基础版本，包含总览、实时、硬件、网络、进程、体检、报告和设备设置；Flutter 移动端仍需完整设备 UI 和商店打包流程。

## 仓库结构

```text
config/                        健康规则
src/shared-contracts/          共享数据契约
src/agent/                     采集、诊断、报告、SQLite、安全、调度
src/agent-service/             HTTPS API、WebSocket、Web 控制台、Windows Service
src/controller-flutter/        Flutter 控制器
src/report-templates/          报告模板说明
tests/PulseCheck.Tests/        无外部框架依赖的自动化测试
installer/windows/             安装/升级/卸载脚本
scripts/                       构建、测试、发布、冒烟测试
docs/                          架构、API、安全、隐私、验收和故障排查
```
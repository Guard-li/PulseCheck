# Changelog

## [0.1.1] - 2026-10-10

### Changed

- CPU 总利用率改用 Windows PDH `Processor Utility/Processor Time`，并保留每核心原生采样。
- 内存使用率按 Windows 可见物理内存计算，与任务管理器/系统内存口径更接近。
- 实时数据先通过 WebSocket 推送，再写入 SQLite，降低界面显示延迟。
- 进程采集扩展为全量进程快照，CPU 按逻辑处理器归一化并按 CPU/内存排序。
- 增加运行时采样周期设置，支持 500ms 高频模式和持久化。
- 进程模块信息仅对返回的排序进程读取，降低每秒采集开销。
- 新增 SMART 管理员授权按钮和独立提权探测入口。
## [0.1.0] - 2026-10-10

### Added

- .NET 8 LTS Windows Agent 与控制台宿主
- CPU、内存、磁盘、网络、NVIDIA GPU、进程、系统、电池和安全状态采集
- SQLite 指标、扫描、报告、调度和告警持久化
- 自签名 TLS、一次性配对码、Token、角色和证书指纹固定
- REST API、WebSocket 实时推送、UDP 设备发现
- 快速/完整体检框架、配置化规则和健康评分
- HTML、JSON、CSV、PDF 报告和 SHA256
- 默认脱敏和报告证据链
- Cron 调度、低功耗/电源条件、超时取消
- 响应式内置 Web 控制台
- Flutter 控制器源码和单元测试
- Windows 服务安装、升级、卸载脚本
- 构建、测试、冒烟测试和 CI 工作流

### Known limitations

- 未在当前环境编译 Flutter APK/Windows 包。
- Intel/AMD GPU 实时利用率、CPU 功耗和部分温度传感器需要后续适配。
- 无 Agent 模式仅能进行有限网络探测。
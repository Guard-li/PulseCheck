# PulseCheck 路线图

## 第一阶段：当前可运行基础版（已完成大部分）

- [x] .NET 8 LTS Agent 服务和 Windows Service 宿主
- [x] CPU、内存、磁盘、网络、GPU、进程和系统采集
- [x] SQLite 历史数据
- [x] HTTPS REST 和 WebSocket
- [x] 一次性配对、长期 Token、角色控制
- [x] 快速/完整扫描框架
- [x] 配置化诊断评分
- [x] HTML、JSON、CSV、PDF 报告
- [x] Cron 调度、任务历史、超时
- [x] 基础告警去重和恢复
- [x] UDP 局域网发现
- [x] 内置 Web 控制台
- [x] Flutter 控制器源码
- [ ] 在安装了 Flutter SDK 的环境完成 Android/Windows 发布包

## 第二阶段：传感器和移动端完善

- [ ] LibreHardwareMonitorLib 可选适配层，明确驱动授权和卸载
- [ ] Intel/AMD GPU 实时利用率和显存
- [ ] Android 后台连接、二维码扫描、报告分享
- [ ] Windows 控制器打包、签名和高 DPI 测试
- [ ] iOS 控制器
- [ ] mDNS/DNS-SD 标准发现

## 第三阶段：远程与集中管理

- [ ] Tailscale/ZeroTier/WireGuard 模式文档和连接向导
- [ ] 多设备集中管理
- [ ] 邮件、Webhook 和移动推送
- [ ] 离线缓存和增量同步
- [ ] 多用户、审计和管理员角色

## 第四阶段：可维护性和发布

- [ ] WiX/Inno Setup 签名安装包
- [ ] 7 天稳定性测试
- [ ] 安装、升级、卸载和睡眠唤醒测试矩阵
- [ ] 性能与低功耗基线
- [ ] 第三方许可证和 SBOM
# 贡献指南

1. 使用 .NET 8 SDK；Windows Service 相关代码只在 Windows 上验证。
2. 先运行 `.\scripts\test.ps1`，确保 7/7 测试和 JS 语法检查通过。
3. 采集器必须返回真实值或明确的不可用状态，禁止返回演示数据。
4. 新增诊断规则时同时添加规则 JSON、证据路径和测试。
5. API 变更必须保持 `/api/v1` 兼容；破坏性变更使用新版本。
6. 所有时间以 UTC 存储，界面按本地时区格式化。
7. 不把 Token、密码、证书私钥、序列号或用户隐私写入普通日志。
8. UI 文案默认中文，并同步提供英文键值。
9. 提交前更新 `CHANGELOG.md`；安全/隐私变化同时更新 `SECURITY.md` 或 `PRIVACY.md`。
10. 不自动安装未签名驱动，不执行破坏性磁盘测试，不默认修改系统安全设置。

## 开发命令

```powershell
dotnet restore PulseCheck.sln
dotnet build PulseCheck.sln -c Debug
dotnet run --project tests/PulseCheck.Tests/PulseCheck.Tests.csproj
node --check src/agent-service/wwwroot/app.js
```
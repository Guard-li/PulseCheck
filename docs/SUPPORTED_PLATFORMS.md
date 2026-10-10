# 支持平台

## Agent

- Windows 10 22H2 x64
- Windows 11 x64
- 目标验证环境：Windows 11 Build 26200、i7-11800H、RTX 3050 Ti、16GB、NVMe + SATA
- Windows Server：未验证，计划中
- Linux/macOS Agent：不支持

## 控制器

- Windows：内置 Web 控制台已可运行；Flutter Windows 源码已提供，尚未本机构建
- Android：Flutter 源码和发现/配对/实时页面基础已提供；APK 未在本机构建
- iOS：第二阶段
- 平板/手机浏览器：响应式 Web 控制台
- 旧版浏览器：建议使用 Chromium/Edge 110+ 或 Firefox 110+

## 开发工具

- .NET 8 SDK（推荐）；已用 .NET 9 SDK 构建 .NET 8 目标
- Node.js 18+（只用于 JavaScript 语法检查）
- Flutter 3.22+、Dart 3.4+（构建控制器时）

## 网络

- 本地局域网、手机热点、USB 网络共享
- UDP 17820：局域网发现
- TCP 17821：HTTPS API/WebSocket
- 远程：建议用户自建 WireGuard、Tailscale 或 ZeroTier，不直接暴露公网端口
# 第三方组件说明

.NET Agent 当前不依赖外部 NuGet 包；SQLite 使用 Windows 自带的 `winsqlite3.dll`，PDF 优先使用操作系统自带的 Microsoft Edge Headless。

Flutter 控制器源码使用以下第三方包（实际许可证以 `pub` 包发布信息为准）：

- `http`
- `web_socket_channel`
- `fl_chart`
- `shared_preferences`
- `flutter_secure_storage`
- `mobile_scanner`
- `qr_flutter`
- `path_provider`
- `share_plus`
- `intl`
- `crypto`
- `flutter_lints`

发布前应运行依赖许可证审计、生成 SBOM，并在安装包中附上第三方许可证。不要将未签名或不明确许可证的驱动程序纳入发布包。
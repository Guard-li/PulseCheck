# PulseCheck Agent API v1

Base URL：`https://<agent-host>:17821/api/v1`

除 `health`、`auth/pair`、`auth/token` 和本机 `pairing/info` 外，所有接口要求：

```http
Authorization: Bearer <token>
```

响应统一为：

```json
{
  "success": true,
  "data": {},
  "error": null,
  "timestampUtc": "2026-10-10T00:00:00Z"
}
```

## 认证

### `GET /health`

服务存活检查，不返回敏感信息。

### `GET /pairing/info`

仅允许回环地址访问，返回设备 ID、显示名、API 地址、证书 SHA256、一次性配对码、配对 URI 和二维码 SVG。

### `POST /auth/pair`

请求：

```json
{
  "pairingCode": "123456",
  "controllerName": "Windows Controller",
  "role": "owner"
}
```

角色支持 `readonly` 和 `owner`。返回 Token 只在本次响应中出现，客户端应放入安全存储。

### `DELETE /auth/devices/{controllerId}`

撤销控制器 Token，需要 `owner`。

## 实时指标

### `GET /metrics/current`

返回最近一次 `MetricSnapshot`。如果 Agent 刚启动且尚无快照，会执行一次真实采集。

### `GET /metrics/history?from=...&to=...&limit=2000`

`from`、`to` 为 ISO 8601 时间；结果按 UTC 升序返回。默认返回最近 1 小时。

### WebSocket `/realtime`

握手子协议：

```text
pulsecheck.v1
token.<长期Token>
```

消息：

```json
{
  "type": "metrics",
  "deviceId": "…",
  "timestampUtc": "…",
  "dataVersion": "2026.10",
  "payload": { "…MetricSnapshot…": "…" }
}
```

告警变更会发送 `type: "alert"`。客户端应在断开后自动重连。

## 设置与管理员授权

### `GET /settings`

返回当前实时采样周期，可选范围为 500 到 10000 毫秒。

### `PUT /settings`

```json
{
  "sampleIntervalMs": 500
}
```

该设置持久化到数据目录，控制台总览会显示最近一次真实采样时间。500 毫秒属于高频采样，会增加资源占用。

### `POST /elevation/smart`

当 Agent 没有管理员权限时，触发 Windows UAC 授权并在授权成功后读取物理磁盘与 SMART。用户取消 UAC 时返回 `authorized=false`，其他监测功能不受影响。该操作属于写入操作，需要 `owner` 角色。
## 系统与硬件

| 方法 | 路径 | 内容 |
| --- | --- | --- |
| GET | `/system/info` | 系统、启动时间、电源、安全状态 |
| GET | `/processes` | Top 10 进程 |
| GET | `/storage` | 卷、物理磁盘、吞吐和 SMART 可用性 |
| GET | `/network` | 网卡、IP、网关、DNS、速率、Wi-Fi 信息 |
| GET | `/sensors` | 温度/功耗及不可用原因 |
| GET | `/security` | Defender、防火墙、Secure Boot、待重启等 |
| GET | `/services?refresh=true` | Windows 服务状态 |
| GET | `/startup?refresh=true` | Run 键和启动文件夹 |
| GET | `/events` | 最近 7 天 System 严重/错误事件 |
| GET | `/software` | 已安装软件清单，敏感路径不返回 |

## 体检和报告

### `POST /scans`

```json
{
  "mode": "quick",
  "intensity": "low",
  "generatePdf": true,
  "redactSensitiveData": true,
  "allowDiskWriteTest": false
}
```

`mode`：`quick` 或 `full`。`allowDiskWriteTest` 当前只作为明确确认字段，默认不执行破坏性磁盘写入。

### `GET /scans`

列出扫描历史。

### `GET /scans/{scanId}`

读取任务状态、进度和报告 ID。

### `DELETE /scans/{scanId}`

取消排队或运行中的任务。

### `GET /reports?limit=100`

列出报告摘要。

### `GET /reports/{reportId}`

读取完整报告 JSON。

### `GET /reports/{reportId}/artifacts/{format}`

`format` = `json`、`html`、`csv`、`pdf`。响应为文件流，并启用断点范围支持。

## 调度

- `GET /schedules`
- `POST /schedules`
- `PUT /schedules/{scheduleId}`
- `DELETE /schedules/{scheduleId}`

Cron 使用 5 段表达式：`分 时 日 月 周`。支持 `*`、`*/n`、单值、范围和逗号列表；默认按 Agent 本机时区计算，内部以 UTC 保存。请求还支持 `runOnlyWhenIdle`、`runOnlyOnAcPower`、`runOnlyOnSpecifiedNetwork`、`networkSsid`、`maximumRuntimeSeconds` 和 `retryCount`。

## 告警

`GET /alerts?includeResolved=true&limit=200`

活动告警按 `ruleId + deviceId` 去重，规则恢复后写入 `status=resolved`。

## 错误码

- `AUTH_REQUIRED`：缺少或 Token 无效
- `OWNER_REQUIRED`：只读控制器尝试写入
- `PAIRING_CODE_INVALID`：配对码错误或过期
- `SCAN_NOT_FOUND`：扫描不存在或已结束
- `REPORT_NOT_FOUND` / `ARTIFACT_NOT_FOUND`：报告或文件不存在
- `INVALID_RANGE` / `INVALID_CRON`：参数错误
- `NO_METRICS`：尚无指标快照
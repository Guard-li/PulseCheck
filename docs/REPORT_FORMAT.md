# 报告格式

## 报告目录

```text
reports/<reportId>/
  PulseCheck-<时间>-<短ID>.html
  PulseCheck-<时间>-<短ID>.json
  PulseCheck-<时间>-<短ID>.csv
  PulseCheck-<时间>-<短ID>.pdf
  pdf-render-warning.txt   # 仅在 PDF 回退时出现
```

## 内容章节

1. 报告 ID、设备名、操作系统、Agent 版本、起止时间、模式和权限
2. 总体分数、风险等级、发现项、原因和建议
3. 评分维度及权重
4. 实时指标快照
5. 系统与硬件概览
6. 原始证据、规则版本和限制
7. 隐私模式、报告 SHA256 和生成时间

## 格式差异

- **JSON**：适合机器读取和二次分析。JSON 文件不包含 `artifacts` 自身列表，避免文件哈希自引用；文件中保留报告核心数据、规则版本和 `reportHash`。
- **HTML**：中文优先、响应式、可直接打印；是 PDF 的源模板。
- **CSV**：UTF-8 BOM，包含 section、metric、value、unit、timestamp、source、ruleId，适合 Excel 和数据分析。
- **PDF**：优先使用 Microsoft Edge Headless 对 HTML 打印；Edge 不可用时生成基础 PDF 并记录警告。正式中文排版应使用 HTML/安装 Edge。

## 脱敏

默认脱敏：

- IP 地址保留前两段
- MAC/BSSID 完全遮盖
- 用户名替换为 `***`
- 序列号只保留最后 4 位
- 设备和进程路径掩码

报告 Hash 是去除 `ReportHash` 和 `Artifacts` 后的 canonical JSON 的 SHA256；每个文件还有独立 SHA256。两者用途不同，前者用于验证报告语义，后者用于验证文件完整性。
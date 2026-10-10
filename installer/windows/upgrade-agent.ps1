#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$PublishDirectory = 'artifacts/win-x64')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$source = Join-Path $root $PublishDirectory
$install = "$env:ProgramFiles\PulseCheck\Agent"
$data = "$env:ProgramData\PulseCheck\data"
$backup = "$env:ProgramData\PulseCheck\backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
if (-not (Test-Path -LiteralPath (Join-Path $source 'PulseCheck.AgentService.exe'))) { throw '请先构建新的发布包。' }
if (Get-Service PulseCheckAgent -ErrorAction SilentlyContinue) { Stop-Service PulseCheckAgent -Force }
New-Item -ItemType Directory -Force -Path $backup | Out-Null
if (Test-Path -LiteralPath $data) { Copy-Item -LiteralPath $data -Destination $backup -Recurse -Force }
& (Join-Path $PSScriptRoot 'install-agent.ps1') -PublishDirectory $PublishDirectory
Write-Host "升级完成。升级前数据备份：$backup"
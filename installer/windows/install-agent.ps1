#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$PublishDirectory = 'artifacts/win-x64',
    [string]$InstallDirectory = "$env:ProgramFiles\PulseCheck\Agent",
    [string]$DataDirectory = "$env:ProgramData\PulseCheck\data",
    [string]$ReportDirectory = "$env:ProgramData\PulseCheck\reports",
    [int]$ApiPort = 17821,
    [int]$DiscoveryPort = 17820
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'PulseCheckAgent'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$source = Join-Path $root $PublishDirectory
if (-not (Test-Path -LiteralPath (Join-Path $source 'PulseCheck.AgentService.exe'))) { throw "未找到发布文件，请先运行 scripts/build.ps1" }
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $InstallDirectory, $DataDirectory, $ReportDirectory | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $InstallDirectory -Recurse -Force
$settingsPath = Join-Path $InstallDirectory 'appsettings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.Agent.ApiPort = $ApiPort
$settings.Agent.DiscoveryPort = $DiscoveryPort
$settings.Agent.DataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)
$settings.Agent.ReportDirectory = [System.IO.Path]::GetFullPath($ReportDirectory)
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
$exe = Join-Path $InstallDirectory 'PulseCheck.AgentService.exe'
if ($existing) {
    & sc.exe config $ServiceName "binPath= `"$exe`" --service" start= delayed-auto | Out-Null
} else {
    & sc.exe create $ServiceName "binPath= `"$exe`" --service" start= delayed-auto DisplayName= "PulseCheck Agent" | Out-Null
}
& sc.exe description $ServiceName "PulseCheck 本地优先设备健康监测服务" | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
if (-not (Get-NetFirewallRule -DisplayName 'PulseCheck Agent (Private)' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'PulseCheck Agent (Private)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ApiPort -Profile Private | Out-Null
}
Start-Service -Name $ServiceName
Write-Host "PulseCheck Agent 已安装。数据目录：$DataDirectory"
Write-Host "浏览器打开 https://localhost:$ApiPort 完成配对。"
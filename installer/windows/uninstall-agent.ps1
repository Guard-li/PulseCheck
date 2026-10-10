#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\PulseCheck\Agent",
    [string]$DataDirectory = "$env:ProgramData\PulseCheck\data",
    [string]$ReportDirectory = "$env:ProgramData\PulseCheck\reports",
    [switch]$KeepData
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'PulseCheckAgent'
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}
Get-NetFirewallRule -DisplayName 'PulseCheck Agent (Private)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
function Remove-VerifiedDirectory([string]$Path, [string]$ExpectedRoot) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($ExpectedRoot).TrimEnd('\') + '\'
    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { throw "拒绝删除未验证目录：$full" }
    Remove-Item -LiteralPath $full -Recurse -Force
}
Remove-VerifiedDirectory $InstallDirectory "$env:ProgramFiles\PulseCheck"
if (-not $KeepData) {
    Remove-VerifiedDirectory $DataDirectory "$env:ProgramData\PulseCheck"
    Remove-VerifiedDirectory $ReportDirectory "$env:ProgramData\PulseCheck"
}
Write-Host 'PulseCheck Agent 已卸载。'
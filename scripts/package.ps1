[CmdletBinding()]
param(
    [string]$BuildDirectory = 'artifacts/win-x64',
    [string]$Version = '0.1.1'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$source = Join-Path $root $BuildDirectory
if (-not (Test-Path -LiteralPath (Join-Path $source 'PulseCheck.AgentService.exe'))) { throw '未找到发布目录，请先运行 scripts/build.ps1' }
$archive = Join-Path $root "artifacts/PulseCheck-$Version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$archive.sha256"
"$hash  $(Split-Path $archive -Leaf)" | Set-Content -LiteralPath $hashFile -Encoding UTF8
Write-Host "发布包：$archive"
Write-Host "SHA256：$hash"
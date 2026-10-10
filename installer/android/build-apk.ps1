[CmdletBinding()]
param([switch]$Release)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) { throw '未安装 Flutter SDK。' }
Push-Location (Join-Path $PSScriptRoot '..\..\src\controller-flutter')
try {
    flutter pub get
    if ($Release) { flutter build apk --release } else { flutter build apk --debug }
} finally { Pop-Location }
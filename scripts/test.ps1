[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
    dotnet restore PulseCheck.sln
    dotnet build PulseCheck.sln -c $Configuration --no-restore
    dotnet run --project tests/PulseCheck.Tests/PulseCheck.Tests.csproj -c $Configuration --no-build
    node --check src/agent-service/wwwroot/app.js
    if (Get-Command flutter -ErrorAction SilentlyContinue) {
        Push-Location src/controller-flutter
        try { flutter pub get; flutter analyze; flutter test } finally { Pop-Location }
    } else { Write-Warning 'Flutter SDK 未安装，Flutter 测试已跳过。' }
} finally { Pop-Location }
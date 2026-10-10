[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$NoFlutter,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
    dotnet restore PulseCheck.sln
    dotnet build PulseCheck.sln -c $Configuration --no-restore
    if (-not $SkipTests) { dotnet run --project tests/PulseCheck.Tests/PulseCheck.Tests.csproj -c $Configuration --no-build }
    node --check src/agent-service/wwwroot/app.js
    if (-not $NoFlutter -and (Get-Command flutter -ErrorAction SilentlyContinue)) {
        Push-Location src/controller-flutter
        try { flutter pub get; flutter analyze; flutter test } finally { Pop-Location }
    } elseif (-not $NoFlutter) {
        Write-Warning '未检测到 Flutter SDK，已跳过控制器构建。安装 Flutter 后运行 scripts/build.ps1。'
    }

    $output = Join-Path $root "artifacts/$Runtime"
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    dotnet publish src/agent-service/PulseCheck.AgentService.csproj -c $Configuration -r $Runtime --self-contained true -p:PublishTrimmed=false -o $output
    $hashFile = Join-Path $output 'SHA256SUMS.txt'
    Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.FullName -ne $hashFile } | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring((($output.TrimEnd('\')) + '\').Length).Replace('\','/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    } | Set-Content -LiteralPath $hashFile -Encoding UTF8
    Write-Host "构建完成：$output"
} finally { Pop-Location }
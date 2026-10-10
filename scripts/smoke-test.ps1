[CmdletBinding()]
param(
    [string]$PublishDirectory = 'artifacts/win-x64',
    [int]$Port = 17821,
    [int]$TimeoutSeconds = 40,
    [switch]$FullScan
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$exe = Join-Path $root (Join-Path $PublishDirectory 'PulseCheck.AgentService.exe')
if (-not (Test-Path -LiteralPath $exe)) { throw "未找到已发布 Agent：$exe，请先运行 scripts/build.ps1" }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http
if (-not ('PulseCheckSmokeHttp' -as [type])) {
    Add-Type -ReferencedAssemblies 'System.Net.Http' -TypeDefinition @"
using System;
using System.Net.Http;
public static class PulseCheckSmokeHttp {
    public static HttpClient CreateClient() {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }
}
"@
}
$client = [PulseCheckSmokeHttp]::CreateClient()
function Invoke-Api {
    param([string]$Method, [string]$Url, [hashtable]$Headers = @{}, [string]$JsonBody = '')
    $request = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($Method)), [Uri]$Url)
    foreach ($key in $Headers.Keys) { $request.Headers.TryAddWithoutValidation($key, $Headers[$key]) | Out-Null }
    if ($JsonBody) { $request.Content = New-Object System.Net.Http.StringContent($JsonBody, [Text.Encoding]::UTF8, 'application/json') }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode)：$text" }
        return $text | ConvertFrom-Json
    } finally { $request.Dispose() }
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exe
$startInfo.WorkingDirectory = Split-Path $exe
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Arguments = "--Agent:ApiPort=$Port"
$process = [System.Diagnostics.Process]::Start($startInfo)
try {
    $base = "https://localhost:$Port/api/v1"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        try { $health = Invoke-Api -Method GET -Url "$base/health"; break } catch { $health = $null }
    } while ((Get-Date) -lt $deadline)
    if ($null -eq $health) { throw 'Agent 未在超时时间内启动' }
    $pairing = Invoke-Api -Method GET -Url "$base/pairing/info"
    $pairBody = @{ pairingCode = $pairing.data.pairingCode; controllerName = 'Smoke Test'; role = 'owner' } | ConvertTo-Json -Compress
    $paired = Invoke-Api -Method POST -Url "$base/auth/pair" -JsonBody $pairBody
    $headers = @{ Authorization = "Bearer $($paired.data.token)" }
    $current = Invoke-Api -Method GET -Url "$base/metrics/current" -Headers $headers
    if ($current.data.memory.totalBytes -le 0) { throw '内存采集返回 0' }
    $scanBody = @{ mode=$(if ($FullScan) { 'full' } else { 'quick' }); intensity='low'; generatePdf=$true; redactSensitiveData=$true } | ConvertTo-Json -Compress
    $scan = Invoke-Api -Method POST -Url "$base/scans" -Headers $headers -JsonBody $scanBody
    do {
        Start-Sleep -Milliseconds 500
        $result = Invoke-Api -Method GET -Url "$base/scans/$($scan.data.scanId)" -Headers $headers
    } while ($result.data.status -in @('queued','running') -and (Get-Date) -lt $deadline)
    if ($result.data.status -ne 'completed') { throw "体检失败：$($result.data.errorMessage)" }
    $reports = Invoke-Api -Method GET -Url "$base/reports?limit=1" -Headers $headers
    if ($reports.data.Count -lt 1) { throw '未生成报告' }
    Write-Host "冒烟测试通过：健康分数 $($reports.data[0].score)，报告 $($reports.data[0].reportId)"
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    $client.Dispose()
}
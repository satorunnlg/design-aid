<#
.SYNOPSIS
    daid mcp を stdio で叩き、MCP プロトコルの疎通を検査する。

.DESCRIPTION
    MCP の仕様は YYYY-MM-DD で改訂され、後方非互換の変更が入る。
    daid 側はプロトコルに触れず SDK に任せているため、**SDK を上げたときに
    実際に喋れているかを確かめる手段が要る**。ビルドが通ることは何の保証にもならない。

    検査するのは 4 点:
      1. server/discover が応答し、対応プロトコル版を返す（2026-07-28 の必須 RPC）
      2. 新仕様（_meta でバージョン折衝）でツールを列挙できる
      3. 新仕様でツールを実行できる
      4. 旧ハンドシェイク（initialize）でもツールを列挙できる（後方互換）

.PARAMETER DaidDll
    テスト対象の daid.dll。既定は Debug ビルド。

.PARAMETER ExpectedToolCount
    期待するツール数。

.EXAMPLE
    .\scripts\test-mcp.ps1
    .\scripts\test-mcp.ps1 -DaidDll .\src\DesignAid\bin\Release\net10.0\daid.dll
#>
param(
    [string]$DaidDll = "",
    [string]$ProtocolVersion = "2026-07-28",
    [int]$ExpectedToolCount = 13
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($DaidDll)) {
    $DaidDll = Join-Path $ProjectRoot "src\DesignAid\bin\Debug\net10.0\daid.dll"
}
if (-not (Test-Path $DaidDll)) {
    Write-Host "[ERROR] daid.dll が見つかりません: $DaidDll" -ForegroundColor Red
    Write-Host "        先に dotnet build を実行してください。" -ForegroundColor Red
    exit 1
}

$TestDir = Join-Path $ProjectRoot ".test-mcp"
$Passed = 0
$Failed = 0

function Add-Result {
    param([bool]$Success, [string]$Name, [string]$Detail = "")
    if ($Success) {
        $script:Passed++
        Write-Host "[OK] $Name" -ForegroundColor Green
    } else {
        $script:Failed++
        Write-Host "[NG] $Name $Detail" -ForegroundColor Red
    }
}

# ---- MCP セッション --------------------------------------------------------

function New-McpSession {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = "dotnet"
    $psi.ArgumentList.Add($DaidDll)
    $psi.ArgumentList.Add("mcp")
    $psi.WorkingDirectory = $TestDir
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardInputEncoding = [System.Text.Encoding]::UTF8
    return [System.Diagnostics.Process]::Start($psi)
}

function Invoke-Rpc {
    <# JSON-RPC を 1 往復する。応答が来なければ $null。 #>
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Method,
        [hashtable]$Params = @{},
        [hashtable]$Meta = $null,
        [int]$Id = 1,
        [int]$TimeoutSec = 30
    )

    $p = $Params.Clone()
    if ($Meta) { $p["_meta"] = $Meta }

    $msg = @{ jsonrpc = "2.0"; method = $Method; id = $Id; params = $p }
    $Process.StandardInput.WriteLine(($msg | ConvertTo-Json -Depth 10 -Compress))
    $Process.StandardInput.Flush()

    $readTask = $Process.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait($TimeoutSec * 1000)) { return $null }
    $line = $readTask.Result
    if ([string]::IsNullOrWhiteSpace($line)) { return $null }
    return $line | ConvertFrom-Json
}

function Stop-McpSession {
    param([System.Diagnostics.Process]$Process)
    try { $Process.StandardInput.Close() } catch { }
    if (-not $Process.WaitForExit(5000)) { try { $Process.Kill() } catch { } }
}

# ---- 準備 ------------------------------------------------------------------

Write-Host "============================================" -ForegroundColor Magenta
Write-Host "  daid mcp プロトコル疎通テスト" -ForegroundColor Magenta
Write-Host "============================================" -ForegroundColor Magenta
Write-Host "対象: $DaidDll"
Write-Host "仕様: $ProtocolVersion"
Write-Host ""

if (Test-Path $TestDir) { Remove-Item -Recurse -Force $TestDir }
New-Item -ItemType Directory -Path $TestDir -Force | Out-Null
& dotnet $DaidDll setup | Out-Null
Push-Location $TestDir

try {
    # ---- 新仕様 ------------------------------------------------------------
    # 2026-07-28 では protocolVersion と clientCapabilities の**両方**が必須。
    # 欠けると -32602 で拒否される。
    $meta = @{
        "io.modelcontextprotocol/protocolVersion"  = $ProtocolVersion
        "io.modelcontextprotocol/clientCapabilities" = @{}
        "io.modelcontextprotocol/clientInfo"       = @{ name = "test-mcp.ps1"; version = "1" }
    }

    $s = New-McpSession
    try {
        $discover = Invoke-Rpc -Process $s -Method "server/discover" -Meta $meta -Id 1
        $versions = $discover.result.supportedVersions
        Add-Result ($null -ne $versions -and $versions -contains $ProtocolVersion) `
            "server/discover が $ProtocolVersion を返す" "(actual: $($versions -join ','))"

        $list = Invoke-Rpc -Process $s -Method "tools/list" -Meta $meta -Id 2
        $count = @($list.result.tools).Count
        Add-Result ($count -eq $ExpectedToolCount) `
            "新仕様で $ExpectedToolCount ツール列挙" "(actual: $count)"

        $call = Invoke-Rpc -Process $s -Method "tools/call" `
            -Params @{ name = "get_status"; arguments = @{} } -Meta $meta -Id 3
        Add-Result ($null -ne $call.result) "新仕様でツール実行が成功" "($($call.error.message))"
    }
    finally { Stop-McpSession -Process $s }

    # ---- 旧ハンドシェイク（後方互換） --------------------------------------
    $s2 = New-McpSession
    try {
        $init = Invoke-Rpc -Process $s2 -Method "initialize" -Id 1 -Params @{
            protocolVersion = "2025-06-18"
            capabilities    = @{}
            clientInfo      = @{ name = "test-mcp.ps1"; version = "1" }
        }
        Add-Result ($null -ne $init.result) "旧 initialize が応答する"

        $s2.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
        $s2.StandardInput.Flush()

        $list2 = Invoke-Rpc -Process $s2 -Method "tools/list" -Id 2
        $count2 = @($list2.result.tools).Count
        Add-Result ($count2 -eq $ExpectedToolCount) `
            "旧ハンドシェイクで $ExpectedToolCount ツール列挙（後方互換）" "(actual: $count2)"
    }
    finally { Stop-McpSession -Process $s2 }
}
finally {
    Pop-Location
    if (Test-Path $TestDir) { Remove-Item -Recurse -Force $TestDir -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Magenta
Write-Host "成功: $Passed / 失敗: $Failed"
Write-Host "============================================" -ForegroundColor Magenta

if ($Failed -gt 0) { exit 1 }
Write-Host "全てのテストが成功しました" -ForegroundColor Green
exit 0

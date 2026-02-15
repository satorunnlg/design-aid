# Design Aid CLI 全コマンドテストスクリプト
# 使用方法: .\scripts\test-all.ps1
#
# このスクリプトはテスト用ディレクトリを作成し、全コマンドをテストします。
# テスト完了後、テストディレクトリはクリーンアップされます。
#
# 注意: dotnet run --project は CWD をプロジェクトディレクトリに変更するため、
# ビルド済み DLL を直接実行します。

param(
    [switch]$SkipBuild,      # ビルドをスキップ
    [switch]$SkipCleanup,    # 最終クリーンアップをスキップ
    [switch]$Verbose,        # 詳細出力
    [switch]$UseGlobalTool   # グローバルツール（daid）を使用
)

$ErrorActionPreference = "Continue"
# スクリプトの場所からプロジェクトルートを取得
if ($PSScriptRoot) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
} else {
    $ProjectRoot = (Get-Location).Path
}
# プロジェクトルートにsrcディレクトリがあるか確認
if (-not (Test-Path (Join-Path $ProjectRoot "src"))) {
    $ProjectRoot = (Get-Location).Path
}

# DLL パス
$DaidDll = Join-Path $ProjectRoot "src/DesignAid/bin/Debug/net10.0/daid.dll"

# テストディレクトリ
$TestDir = Join-Path $ProjectRoot ".test-integration"

# 色付き出力関数
function Write-Phase {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "`n--- $Message ---" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Gray
}

# コマンド実行関数
function Invoke-Daid {
    param(
        [string]$Arguments,
        [string]$Description,
        [switch]$AllowFailure,
        [string]$Input
    )

    Write-Step $Description
    Write-Info "daid $Arguments"

    if ($UseGlobalTool) {
        $cmdArgs = $Arguments.Split(' ')
        if ($Input) {
            $result = Write-Output $Input | & daid $cmdArgs 2>&1
        } else {
            $result = & daid $cmdArgs 2>&1
        }
    } else {
        $cmdArgs = @($DaidDll) + $Arguments.Split(' ')
        if ($Input) {
            $result = Write-Output $Input | & dotnet $cmdArgs 2>&1
        } else {
            $result = & dotnet $cmdArgs 2>&1
        }
    }

    if ($Verbose) {
        Write-Host $result
    } else {
        # 最初の10行だけ表示
        $result | Select-Object -First 10 | ForEach-Object { Write-Host $_ }
        if (($result | Measure-Object -Line).Lines -gt 10) {
            Write-Host "... (省略)" -ForegroundColor DarkGray
        }
    }

    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        Write-Error "コマンドが失敗しました: daid $Arguments"
        return $false
    }

    Write-Success $Description
    return $true
}

# テスト結果追跡
$TestResults = @{
    Passed = 0
    Failed = 0
    Skipped = 0
}

function Add-TestResult {
    param(
        [bool]$Success,
        [string]$Name
    )
    if ($Success) {
        $script:TestResults.Passed++
    } else {
        $script:TestResults.Failed++
        Write-Error "テスト失敗: $Name"
    }
}

# メイン処理開始
Write-Host @"
============================================
  Design Aid CLI 全コマンドテスト
============================================
"@ -ForegroundColor Magenta

Write-Info "プロジェクトルート: $ProjectRoot"
Write-Info "テストディレクトリ: $TestDir"

# Phase 0: ビルドとツールインストール
Write-Phase "Phase 0: ビルド"

if (-not $SkipBuild) {
    Write-Step "ビルド実行"
    dotnet build "$ProjectRoot" --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "ビルドに失敗しました"
        exit 1
    }
    Write-Success "ビルド完了"

    if ($UseGlobalTool) {
        Write-Step "グローバルツールの更新"
        dotnet tool update --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*" 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Info "新規インストールを試行..."
            dotnet tool install --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*"
        }
        Write-Success "ツールインストール完了"
    }
} else {
    Write-Info "ビルドをスキップしました"
}

# DLL の存在確認
if (-not $UseGlobalTool -and -not (Test-Path $DaidDll)) {
    Write-Error "DLL が見つかりません: $DaidDll"
    Write-Error "先にビルドを実行してください: dotnet build"
    exit 1
}

# テストディレクトリの初期化
Write-Step "テストディレクトリのクリーンアップ"
if (Test-Path $TestDir) {
    Remove-Item -Recurse -Force $TestDir
    Write-Info "既存のテストディレクトリを削除しました"
}
New-Item -ItemType Directory -Path $TestDir -Force | Out-Null
Write-Info "テストディレクトリを作成: $TestDir"

# テストディレクトリに移動
Set-Location $TestDir

# Phase 1: 初期化 (Setup & Config)
Write-Phase "Phase 1: 初期化 (Setup & Config)"

Add-TestResult (Invoke-Daid "--help" "ヘルプの確認") "help"
Add-TestResult (Invoke-Daid "setup" "プロジェクトディレクトリの初期化") "setup"
Add-TestResult (Invoke-Daid "config show" "設定の表示") "config show"
Add-TestResult (Invoke-Daid "config path" "パス情報の表示") "config path"
Add-TestResult (Invoke-Daid "config set vector_search.enabled true" "ベクトル検索有効化") "config set"

# Phase 2: 装置（Asset）管理
Write-Phase "Phase 2: 装置（Asset）管理"

Add-TestResult (Invoke-Daid "asset list" "装置一覧（空）") "asset list empty"
Add-TestResult (Invoke-Daid "asset add lifting-unit --display-name 昇降ユニット" "装置追加: lifting-unit") "asset add 1"
Add-TestResult (Invoke-Daid "asset add control-panel --display-name 制御盤" "装置追加: control-panel") "asset add 2"
Add-TestResult (Invoke-Daid "asset add safety-module --display-name 安全モジュール" "装置追加: safety-module") "asset add 3"
Add-TestResult (Invoke-Daid "asset list" "装置一覧（3件）") "asset list"
Add-TestResult (Invoke-Daid "asset list --verbose" "装置一覧（詳細）") "asset list verbose"
Add-TestResult (Invoke-Daid "asset link lifting-unit --child safety-module --quantity 1 --notes 安全装置" "子装置リンク") "asset link"

# Phase 3: パーツ（Part）管理
Write-Phase "Phase 3: パーツ（Part）管理"

Add-TestResult (Invoke-Daid "part list" "パーツ一覧（空）") "part list empty"
Add-TestResult (Invoke-Daid "part add BASE-PLATE-001 --type Fabricated --name ベースプレート" "パーツ追加: BASE-PLATE-001") "part add 1"
Add-TestResult (Invoke-Daid "part add MTR-001 --type Purchased --name サーボモーター" "パーツ追加: MTR-001") "part add 2"
Add-TestResult (Invoke-Daid "part add BOLT-M10-30 --type Standard --name 六角ボルトM10x30" "パーツ追加: BOLT-M10-30") "part add 3"
Add-TestResult (Invoke-Daid "part list" "パーツ一覧（3件）") "part list"
Add-TestResult (Invoke-Daid "part link BASE-PLATE-001 --asset lifting-unit --quantity 1" "パーツリンク: BASE-PLATE-001") "part link 1"
Add-TestResult (Invoke-Daid "part link MTR-001 --asset lifting-unit --quantity 2" "パーツリンク: MTR-001") "part link 2"
Add-TestResult (Invoke-Daid "part link BOLT-M10-30 --asset lifting-unit --quantity 8" "パーツリンク: BOLT-M10-30") "part link 3"

# Phase 4: コア機能
Write-Phase "Phase 4: コア機能"

Add-TestResult (Invoke-Daid "status" "ステータス確認") "status"
Add-TestResult (Invoke-Daid "check" "整合性チェック") "check"
Add-TestResult (Invoke-Daid "sync" "同期") "sync"
Add-TestResult (Invoke-Daid "verify" "設計基準検証") "verify"

# Phase 5: 検索
Write-Phase "Phase 5: 検索"

Add-TestResult (Invoke-Daid "sync --include-vectors" "ベクトルインデックス構築") "sync vectors"
Add-TestResult (Invoke-Daid "search ベースプレート" "キーワード検索" -AllowFailure) "search"

# Phase 6: 手配
Write-Phase "Phase 6: 手配（Deploy）"

Add-TestResult (Invoke-Daid "deploy --dry-run" "手配パッケージ確認（成果物なし）") "deploy dry-run"

# 成果物を追加
Write-Step "成果物を追加"
$drawingPath = Join-Path $TestDir "components/BASE-PLATE-001/drawing.dxf"
if (-not (Test-Path (Split-Path $drawingPath))) {
    New-Item -ItemType Directory -Path (Split-Path $drawingPath) -Force | Out-Null
}
"テスト図面" | Out-File -FilePath $drawingPath -Encoding UTF8
Write-Success "成果物を追加しました"

Add-TestResult (Invoke-Daid "sync" "成果物の同期") "sync artifact"
Add-TestResult (Invoke-Daid "deploy --dry-run" "手配パッケージ確認（成果物あり）") "deploy dry-run with artifact"

# Phase 7: アーカイブ
Write-Phase "Phase 7: アーカイブ（容量節約）"

Add-TestResult (Invoke-Daid "archive list" "アーカイブ一覧（空）") "archive list empty"
Add-TestResult (Invoke-Daid "archive asset control-panel" "装置をアーカイブ") "archive asset"
Add-TestResult (Invoke-Daid "archive part BOLT-M10-30" "パーツをアーカイブ") "archive part"
Add-TestResult (Invoke-Daid "archive list" "アーカイブ一覧（2件）") "archive list"
Add-TestResult (Invoke-Daid "archive list --json" "アーカイブ一覧（JSON）") "archive list json"

# アーカイブ後の一覧確認
Write-Step "アーカイブ後の一覧確認"
if ($UseGlobalTool) {
    & daid asset list
    & daid part list
} else {
    & dotnet $DaidDll asset list
    & dotnet $DaidDll part list
}
Write-Success "アーカイブ後の一覧確認完了"

Add-TestResult (Invoke-Daid "archive restore part BOLT-M10-30" "パーツを復元") "archive restore part"
Add-TestResult (Invoke-Daid "archive restore asset control-panel" "装置を復元") "archive restore asset"
Add-TestResult (Invoke-Daid "archive list" "アーカイブ一覧（空確認）") "archive list after restore"

# Phase 8: バックアップ
Write-Phase "Phase 8: バックアップ"

Add-TestResult (Invoke-Daid "backup --local-only" "ローカルバックアップ") "backup"

# バックアップファイル名を取得
$backupFile = Get-ChildItem -Path $TestDir -Filter "design-aid-backup_*.zip" | Select-Object -First 1
if ($backupFile) {
    Write-Success "バックアップファイル: $($backupFile.Name)"
} else {
    Write-Error "バックアップファイルが見つかりません"
}

# Phase 9: クリーンアップ（削除操作）
Write-Phase "Phase 9: クリーンアップ（削除操作）"

Add-TestResult (Invoke-Daid "asset unlink lifting-unit --child safety-module" "子装置リンク解除") "asset unlink"
Add-TestResult (Invoke-Daid "part remove BOLT-M10-30" "パーツ削除: BOLT-M10-30" -Input "y") "part remove 1"
Add-TestResult (Invoke-Daid "part remove MTR-001" "パーツ削除: MTR-001" -Input "y") "part remove 2"
Add-TestResult (Invoke-Daid "part remove BASE-PLATE-001" "パーツ削除: BASE-PLATE-001" -Input "y") "part remove 3"
Add-TestResult (Invoke-Daid "part list" "パーツ一覧（空確認）") "part list empty after remove"
Add-TestResult (Invoke-Daid "asset remove safety-module" "装置削除: safety-module" -Input "y") "asset remove 1"
Add-TestResult (Invoke-Daid "asset remove control-panel" "装置削除: control-panel" -Input "y") "asset remove 2"
Add-TestResult (Invoke-Daid "asset remove lifting-unit" "装置削除: lifting-unit" -Input "y") "asset remove 3"
Add-TestResult (Invoke-Daid "asset list" "装置一覧（空確認）") "asset list empty after remove"

# Phase 10: 復元テスト
Write-Phase "Phase 10: 復元テスト"

if ($backupFile) {
    Add-TestResult (Invoke-Daid "restore $($backupFile.FullName)" "バックアップから復元" -Input "y") "restore"
    Add-TestResult (Invoke-Daid "asset list" "復元後の装置一覧") "asset list after restore"
    Add-TestResult (Invoke-Daid "part list" "復元後のパーツ一覧") "part list after restore"

    # 再クリーンアップ
    Write-Step "再クリーンアップ"
    if ($UseGlobalTool) {
        "y" | & daid part remove BOLT-M10-30 2>$null
        "y" | & daid part remove MTR-001 2>$null
        "y" | & daid part remove BASE-PLATE-001 2>$null
        "y" | & daid asset remove safety-module 2>$null
        "y" | & daid asset remove control-panel 2>$null
        "y" | & daid asset remove lifting-unit 2>$null
    } else {
        "y" | & dotnet $DaidDll part remove BOLT-M10-30 2>$null
        "y" | & dotnet $DaidDll part remove MTR-001 2>$null
        "y" | & dotnet $DaidDll part remove BASE-PLATE-001 2>$null
        "y" | & dotnet $DaidDll asset remove safety-module 2>$null
        "y" | & dotnet $DaidDll asset remove control-panel 2>$null
        "y" | & dotnet $DaidDll asset remove lifting-unit 2>$null
    }
    Write-Success "再クリーンアップ完了"
} else {
    Write-Info "バックアップファイルがないため復元テストをスキップ"
    $TestResults.Skipped++
}

# Phase 11: 完全クリーンアップ
Write-Phase "Phase 11: 完全クリーンアップ"

# プロジェクトルートに戻る
Set-Location $ProjectRoot

if (-not $SkipCleanup) {
    Write-Step "テストディレクトリの削除"
    if (Test-Path $TestDir) {
        Remove-Item -Recurse -Force $TestDir
        Write-Success "テストディレクトリを削除しました"
    }
} else {
    Write-Info "クリーンアップをスキップしました（テストディレクトリは残っています）"
}

# テスト結果サマリー
Write-Host @"

============================================
  テスト結果サマリー
============================================
"@ -ForegroundColor Magenta

Write-Host "成功: $($TestResults.Passed)" -ForegroundColor Green
Write-Host "失敗: $($TestResults.Failed)" -ForegroundColor $(if ($TestResults.Failed -gt 0) { "Red" } else { "Green" })
Write-Host "スキップ: $($TestResults.Skipped)" -ForegroundColor Yellow

if ($TestResults.Failed -gt 0) {
    Write-Host "`nテストに失敗しました。" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`n全てのテストが成功しました！" -ForegroundColor Green
    exit 0
}

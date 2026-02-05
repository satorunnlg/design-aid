#!/bin/bash
# Design Aid CLI 全コマンドテストスクリプト
# 使用方法: ./scripts/test-all.sh
#
# このスクリプトは data/ ディレクトリを初期化し、全コマンドをテストします。
# テスト完了後、data/ ディレクトリはクリーンアップされます。

set -e

# オプション
SKIP_BUILD=false
SKIP_CLEANUP=false
VERBOSE=false

while [[ "$#" -gt 0 ]]; do
    case $1 in
        --skip-build) SKIP_BUILD=true ;;
        --skip-cleanup) SKIP_CLEANUP=true ;;
        --verbose|-v) VERBOSE=true ;;
        *) echo "不明なオプション: $1"; exit 1 ;;
    esac
    shift
done

# プロジェクトルートへ移動
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_ROOT"

# カラー定義
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# 出力関数
write_phase() {
    echo -e "\n${CYAN}========================================${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${CYAN}========================================${NC}"
}

write_step() {
    echo -e "\n${YELLOW}--- $1 ---${NC}"
}

write_success() {
    echo -e "${GREEN}[OK] $1${NC}"
}

write_error() {
    echo -e "${RED}[ERROR] $1${NC}"
}

write_info() {
    echo -e "${GRAY}[INFO] $1${NC}"
}

# テスト結果追跡
TESTS_PASSED=0
TESTS_FAILED=0
TESTS_SKIPPED=0

add_test_result() {
    local success=$1
    local name=$2
    if [ "$success" = "true" ]; then
        ((TESTS_PASSED++))
    else
        ((TESTS_FAILED++))
        write_error "テスト失敗: $name"
    fi
}

# コマンド実行関数
invoke_daid() {
    local args="$1"
    local description="$2"
    local allow_failure="${3:-false}"
    local input="${4:-}"

    write_step "$description"
    write_info "daid $args"

    set +e
    if [ -n "$input" ]; then
        result=$(echo "$input" | daid $args 2>&1)
    else
        result=$(daid $args 2>&1)
    fi
    exit_code=$?
    set -e

    if [ "$VERBOSE" = "true" ]; then
        echo "$result"
    else
        echo "$result" | head -10
        line_count=$(echo "$result" | wc -l)
        if [ "$line_count" -gt 10 ]; then
            echo -e "${GRAY}... (省略)${NC}"
        fi
    fi

    if [ $exit_code -ne 0 ] && [ "$allow_failure" != "true" ]; then
        write_error "コマンドが失敗しました: daid $args"
        add_test_result "false" "$description"
        return 1
    fi

    write_success "$description"
    add_test_result "true" "$description"
    return 0
}

# メイン処理開始
echo -e "${MAGENTA}============================================${NC}"
echo -e "${MAGENTA}  Design Aid CLI 全コマンドテスト${NC}"
echo -e "${MAGENTA}============================================${NC}"

write_info "プロジェクトルート: $PROJECT_ROOT"

# Phase 0: ビルドとツールインストール
write_phase "Phase 0: ビルドとツールインストール"

if [ "$SKIP_BUILD" != "true" ]; then
    write_step "ビルド実行"
    dotnet build --nologo -v q
    write_success "ビルド完了"

    write_step "グローバルツールの更新"
    dotnet tool update --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*" 2>/dev/null || \
        dotnet tool install --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*"
    write_success "ツールインストール完了"
else
    write_info "ビルドをスキップしました"
fi

# data ディレクトリの初期化
write_step "data ディレクトリのクリーンアップ"
if [ -d "data" ]; then
    rm -rf data
    write_info "既存の data/ を削除しました"
fi

# バックアップファイルの削除
rm -f design-aid-backup_*.zip 2>/dev/null || true

# Phase 1: 初期化 (Setup & Config)
write_phase "Phase 1: 初期化 (Setup & Config)"

invoke_daid "--help" "ヘルプの確認"
invoke_daid "setup" "データディレクトリの初期化"
invoke_daid "config show" "設定の表示"
invoke_daid "config path" "パス情報の表示"
invoke_daid "config set qdrant.enabled false" "Qdrant無効化"

# Phase 2: 装置（Asset）管理
write_phase "Phase 2: 装置（Asset）管理"

invoke_daid "asset list" "装置一覧（空）"
invoke_daid "asset add lifting-unit --display-name 昇降ユニット" "装置追加: lifting-unit"
invoke_daid "asset add control-panel --display-name 制御盤" "装置追加: control-panel"
invoke_daid "asset add safety-module --display-name 安全モジュール" "装置追加: safety-module"
invoke_daid "asset list" "装置一覧（3件）"
invoke_daid "asset list --verbose" "装置一覧（詳細）"
invoke_daid "asset link lifting-unit --child safety-module --quantity 1 --notes 安全装置" "子装置リンク"

# Phase 3: パーツ（Part）管理
write_phase "Phase 3: パーツ（Part）管理"

invoke_daid "part list" "パーツ一覧（空）"
invoke_daid "part add BASE-PLATE-001 --type Fabricated --name ベースプレート" "パーツ追加: BASE-PLATE-001"
invoke_daid "part add MTR-001 --type Purchased --name サーボモーター" "パーツ追加: MTR-001"
invoke_daid "part add BOLT-M10-30 --type Standard --name 六角ボルトM10x30" "パーツ追加: BOLT-M10-30"
invoke_daid "part list" "パーツ一覧（3件）"
invoke_daid "part link BASE-PLATE-001 --asset lifting-unit --quantity 1" "パーツリンク: BASE-PLATE-001"
invoke_daid "part link MTR-001 --asset lifting-unit --quantity 2" "パーツリンク: MTR-001"
invoke_daid "part link BOLT-M10-30 --asset lifting-unit --quantity 8" "パーツリンク: BOLT-M10-30"

# Phase 4: コア機能
write_phase "Phase 4: コア機能"

invoke_daid "status" "ステータス確認"
invoke_daid "check" "整合性チェック"
invoke_daid "sync" "同期"
invoke_daid "verify" "設計基準検証"

# Phase 5: 検索
write_phase "Phase 5: 検索"

invoke_daid "search ベースプレート" "キーワード検索" "true"

# Phase 6: 手配
write_phase "Phase 6: 手配（Deploy）"

invoke_daid "deploy --dry-run" "手配パッケージ確認（成果物なし）"

# 成果物を追加
write_step "成果物を追加"
mkdir -p data/components/BASE-PLATE-001
echo "テスト図面" > data/components/BASE-PLATE-001/drawing.dxf
write_success "成果物を追加しました"

invoke_daid "deploy --dry-run" "手配パッケージ確認（成果物あり）"

# Phase 7: アーカイブ
write_phase "Phase 7: アーカイブ（容量節約）"

invoke_daid "archive list" "アーカイブ一覧（空）"
invoke_daid "archive asset control-panel" "装置をアーカイブ"
invoke_daid "archive part BOLT-M10-30" "パーツをアーカイブ"
invoke_daid "archive list" "アーカイブ一覧（2件）"
invoke_daid "archive list --json" "アーカイブ一覧（JSON）"

# アーカイブ後の一覧確認
write_step "アーカイブ後の一覧確認"
daid asset list
daid part list
write_success "アーカイブ後の一覧確認完了"

invoke_daid "archive restore part BOLT-M10-30" "パーツを復元"
invoke_daid "archive restore asset control-panel" "装置を復元"
invoke_daid "archive list" "アーカイブ一覧（空確認）"

# Phase 8: バックアップ
write_phase "Phase 8: バックアップ"

invoke_daid "backup --local-only" "ローカルバックアップ"

# バックアップファイル名を取得
BACKUP_FILE=$(ls -1 design-aid-backup_*.zip 2>/dev/null | head -1)
if [ -n "$BACKUP_FILE" ]; then
    write_success "バックアップファイル: $BACKUP_FILE"
else
    write_error "バックアップファイルが見つかりません"
fi

# Phase 9: クリーンアップ（削除操作）
write_phase "Phase 9: クリーンアップ（削除操作）"

invoke_daid "asset unlink lifting-unit --child safety-module" "子装置リンク解除"
invoke_daid "part remove BOLT-M10-30" "パーツ削除: BOLT-M10-30" "false" "y"
invoke_daid "part remove MTR-001" "パーツ削除: MTR-001" "false" "y"
invoke_daid "part remove BASE-PLATE-001" "パーツ削除: BASE-PLATE-001" "false" "y"
invoke_daid "part list" "パーツ一覧（空確認）"
invoke_daid "asset remove safety-module" "装置削除: safety-module" "false" "y"
invoke_daid "asset remove control-panel" "装置削除: control-panel" "false" "y"
invoke_daid "asset remove lifting-unit" "装置削除: lifting-unit" "false" "y"
invoke_daid "asset list" "装置一覧（空確認）"

# Phase 10: 復元テスト
write_phase "Phase 10: 復元テスト"

if [ -n "$BACKUP_FILE" ]; then
    invoke_daid "restore ./$BACKUP_FILE" "バックアップから復元" "false" "y"
    invoke_daid "asset list" "復元後の装置一覧"
    invoke_daid "part list" "復元後のパーツ一覧"

    # 再クリーンアップ
    write_step "再クリーンアップ"
    echo "y" | daid part remove BOLT-M10-30 2>/dev/null || true
    echo "y" | daid part remove MTR-001 2>/dev/null || true
    echo "y" | daid part remove BASE-PLATE-001 2>/dev/null || true
    echo "y" | daid asset remove safety-module 2>/dev/null || true
    echo "y" | daid asset remove control-panel 2>/dev/null || true
    echo "y" | daid asset remove lifting-unit 2>/dev/null || true
    write_success "再クリーンアップ完了"
else
    write_info "バックアップファイルがないため復元テストをスキップ"
    ((TESTS_SKIPPED++))
fi

# Phase 11: 完全クリーンアップ
write_phase "Phase 11: 完全クリーンアップ"

if [ "$SKIP_CLEANUP" != "true" ]; then
    write_step "バックアップファイルの削除"
    rm -f design-aid-backup_*.zip
    write_success "バックアップファイルを削除しました"

    write_step "data ディレクトリの削除"
    if [ -d "data" ]; then
        rm -rf data
        write_success "data/ ディレクトリを削除しました"
    fi
else
    write_info "クリーンアップをスキップしました（data/ は残っています）"
fi

# テスト結果サマリー
echo -e "\n${MAGENTA}============================================${NC}"
echo -e "${MAGENTA}  テスト結果サマリー${NC}"
echo -e "${MAGENTA}============================================${NC}"

echo -e "${GREEN}成功: $TESTS_PASSED${NC}"
if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "${RED}失敗: $TESTS_FAILED${NC}"
else
    echo -e "${GREEN}失敗: $TESTS_FAILED${NC}"
fi
echo -e "${YELLOW}スキップ: $TESTS_SKIPPED${NC}"

if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "\n${RED}テストに失敗しました。${NC}"
    exit 1
else
    echo -e "\n${GREEN}全てのテストが成功しました！${NC}"
    exit 0
fi

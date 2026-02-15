# Design Aid (DAID)

[![CI](https://github.com/satorunnlg/design-aid/actions/workflows/ci.yml/badge.svg)](https://github.com/satorunnlg/design-aid/actions/workflows/ci.yml)
[![Release](https://github.com/satorunnlg/design-aid/actions/workflows/release.yml/badge.svg)](https://github.com/satorunnlg/design-aid/actions/workflows/release.yml)
[![GitHub release](https://img.shields.io/github/v/release/satorunnlg/design-aid?include_prereleases)](https://github.com/satorunnlg/design-aid/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](https://github.com/satorunnlg/design-aid/releases)

機械設計における「設計の論理（理）」と「物理的な手配」の乖離を埋めるためのサポートシステム。

## 概要

Design Aid は、AWS CDK の思想を機械設計に適用し、**手配境界**を抽象化の基準として、設計の整合性と知見の継承を支援する CLI ツールです。

### 設計哲学

| 原則 | 説明 |
|------|------|
| **Support, Not Control** | 設計者を縛るのではなく、ミスの検知や過去の知見の提示を通じて「助ける」存在 |
| **Procurement Boundary** | 自社から他社へ手配（発注）するタイミングをシステムの境界とする |
| **Hash-Based Integrity** | 全ての成果物（図面・計算書）をハッシュ値で管理し、不整合を根絶する |

## インストール

### GitHub Release からダウンロード（推奨）

[Releases](https://github.com/satorunnlg/design-aid/releases) ページから、お使いの OS に対応したバイナリをダウンロードしてください。

| OS | ファイル |
|----|----------|
| Windows (x64) | `design-aid-win-x64.zip` |
| Linux (x64) | `design-aid-linux-x64.tar.gz` |
| macOS (x64) | `design-aid-osx-x64.tar.gz` |
| macOS (ARM64) | `design-aid-osx-arm64.tar.gz` |

### .NET グローバルツールとしてインストール

```bash
# NuGet パッケージからインストール（リリース後に利用可能）
dotnet tool install --global DesignAid
```

### ソースからビルド

```bash
# リポジトリのクローン
git clone https://github.com/satorunnlg/design-aid.git
cd design-aid

# ビルド
dotnet build

# グローバルツールとしてインストール
dotnet pack src/DesignAid
dotnet tool install --global --add-source ./src/DesignAid/bin/Release DesignAid
```

## クイックスタート

```bash
# データディレクトリの初期化
daid setup

# 装置を追加
daid asset add lifting-unit --display-name "昇降ユニット"

# パーツを追加
daid part add SP-2026-PLATE-01 --name "ベースプレート" --type Fabricated

# パーツを装置に紐付け
daid part link SP-2026-PLATE-01 --asset lifting-unit

# 整合性チェック
daid check

# 状態確認
daid status
```

## 主なコマンド

### 装置管理

```bash
daid asset add <name>                     # 装置を追加（git init 付き）
daid asset add <name> --no-git            # 装置を追加（git init なし）
daid asset list                           # 装置一覧を表示
daid asset list --verbose                 # 装置一覧を詳細表示
daid asset remove <name>                  # 装置を削除
daid asset link <parent> --child <child>  # 子装置を組み込み
daid asset unlink <parent> --child <child> # 子装置リンクを解除
```

### パーツ管理

```bash
daid part add <part-number>               # パーツを追加
daid part list                            # パーツ一覧を表示
daid part link <part-number> --asset <name> # パーツを装置に紐付け
daid part remove <part-number>            # パーツを削除
```

### 整合性・検証

```bash
daid check   # ファイルハッシュの整合性検証
daid verify  # 設計基準に基づくバリデーション
daid sync    # ファイルシステムと DB の同期
```

### 手配・検索

```bash
daid deploy            # 手配パッケージの作成
daid deploy --dry-run  # 手配パッケージの確認（ドライラン）
daid search <query>    # 類似設計のベクトル検索
```

### バックアップ・復元

```bash
daid backup             # データをバックアップ（ZIP/S3）
daid backup --local-only # ローカル ZIP のみ作成
daid restore <source>   # バックアップから復元
```

### アーカイブ（容量節約）

```bash
daid archive asset <name>           # 装置をアーカイブ
daid archive part <part-number>     # パーツをアーカイブ
daid archive list                   # アーカイブ一覧を表示
daid archive restore asset <name>   # 装置を復元
daid archive restore part <part-number> # パーツを復元
```

### システム管理

```bash
daid setup   # データディレクトリの初期化
daid status  # システム状態の表示
daid config show  # 設定の表示
daid config set <key> <value>  # 設定の変更
daid update  # ツールを最新版に更新
```

## 技術スタック

- **言語**: C# 13 / .NET 10.0
- **CLI**: System.CommandLine 2.0
- **ORM**: Entity Framework Core 10.0 (SQLite)
- **ベクトル検索**: SQLite BLOB + HNSW（組み込み、外部依存なし）

## 前提条件

- .NET 10.0 SDK（ソースからビルドする場合）

## ベクトル検索

類似設計検索は SQLite + HNSW ライブラリで組み込み実装されており、外部サービス（Docker 等）は不要です。

```bash
# ベクトルインデックスの構築
daid sync --include-vectors

# 類似設計の検索
daid search "油圧シリンダ"
```

## 開発

```bash
# テスト実行
dotnet test

# CLI 統合テスト（Windows）
.\scripts\test-all.ps1

# CLI 統合テスト（Linux/macOS）
./scripts/test-all.sh

# フォーマット
dotnet format
```

## ドキュメント

- [docs/COMMAND_REFERENCE.md](./docs/COMMAND_REFERENCE.md) - コマンドリファレンス（全コマンド・設定キー・データ構造の詳細）
- [DESIGN.md](./DESIGN.md) - 詳細な設計ドキュメント
- [CONTRIBUTING.md](./CONTRIBUTING.md) - コントリビューションガイド
- [CHANGELOG.md](./CHANGELOG.md) - 変更履歴
- [docs/TEST_SCENARIO.md](./docs/TEST_SCENARIO.md) - テストシナリオ

## コントリビューション

プロジェクトへの貢献を歓迎します！詳細は [CONTRIBUTING.md](./CONTRIBUTING.md) を参照してください。

## ライセンス

[MIT License](./LICENSE)

## サポート

- **バグ報告・機能要望**: [GitHub Issues](https://github.com/satorunnlg/design-aid/issues)
- **ディスカッション**: [GitHub Discussions](https://github.com/satorunnlg/design-aid/discussions)

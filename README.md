# Design Aid (DAID)

機械設計における「設計の論理（理）」と「物理的な手配」の乖離を埋めるためのサポートシステム。

## 概要

Design Aid は、AWS CDK の思想を機械設計に適用し、**手配境界**を抽象化の基準として、設計の整合性と知見の継承を支援する CLI ツールです。

### 設計哲学

| 原則 | 説明 |
|------|------|
| **Support, Not Control** | 設計者を縛るのではなく、ミスの検知や過去の知見の提示を通じて「助ける」存在 |
| **Procurement Boundary** | 自社から他社へ手配（発注）するタイミングをシステムの境界とする |
| **Hash-Based Integrity** | 全ての成果物（図面・計算書）をハッシュ値で管理し、不整合を根絶する |

## 主な機能

### 装置管理
- `daid asset add <name>` - 装置を追加（git init 付き）
- `daid asset add <name> --no-git` - 装置を追加（git init なし）
- `daid asset list` - 装置一覧を表示
- `daid asset list --verbose` - 装置一覧を詳細表示
- `daid asset remove <name>` - 装置を削除
- `daid asset link <parent> --child <child>` - 子装置を組み込み
- `daid asset unlink <parent> --child <child>` - 子装置リンクを解除

### パーツ管理
- `daid part add <part-number>` - パーツを追加（git init 付き）
- `daid part add <part-number> --no-git` - パーツを追加（git init なし）
- `daid part list` - パーツ一覧を表示
- `daid part link <part-number> --asset <name>` - パーツを装置に紐付け
- `daid part remove <part-number>` - パーツを削除

### 整合性・検証
- `daid check` - ファイルハッシュの整合性検証
- `daid verify` - 設計基準に基づくバリデーション
- `daid sync` - ファイルシステムと DB の同期

### 手配・検索
- `daid deploy` - 手配パッケージの作成
- `daid deploy --dry-run` - 手配パッケージの確認（ドライラン）
- `daid search <query>` - 類似設計のベクトル検索

### バックアップ・復元
- `daid backup` - データをバックアップ（ZIP/S3）
- `daid backup --local-only` - ローカル ZIP のみ作成
- `daid restore <source>` - バックアップから復元

### 状態確認
- `daid status` - システム状態の表示

## 技術スタック

- **言語**: C# 13 / .NET 10.0
- **CLI**: System.CommandLine 2.0
- **ORM**: Entity Framework Core 10.0 (SQLite)
- **Vector DB**: Qdrant 1.x

## セットアップ

### 前提条件

- .NET 10.0 SDK
- Docker (Qdrant 用、オプション)

### インストール

```bash
# リポジトリのクローン
git clone https://github.com/your-org/design-aid.git
cd design-aid

# 依存関係の復元
dotnet restore

# ビルド
dotnet build

# グローバルツールとしてインストール（オプション）
dotnet pack src/DesignAid
dotnet tool install --global --add-source ./src/DesignAid/bin/Release DesignAid
```

### データディレクトリの初期化

```bash
# データディレクトリを初期化
daid setup

# または dotnet run で実行
dotnet run --project src/DesignAid -- setup
```

### Qdrant（オプション）

類似設計検索を使用する場合：

```bash
# Qdrant の起動
docker compose up -d
```

## 使用例

```bash
# ヘルプ表示
daid --help

# データディレクトリの初期化
daid setup

# 装置を追加
daid asset add lifting-unit --display-name "昇降ユニット"

# パーツ追加
daid part add SP-2026-PLATE-01 --name "ベースプレート" --type Fabricated

# パーツを装置に紐付け
daid part link SP-2026-PLATE-01 --asset lifting-unit

# パーツ一覧
daid part list

# 整合性チェック
daid check

# 状態確認
daid status

# バックアップ（ローカル）
daid backup --local-only

# 復元
daid restore ./design-aid-backup_20260205.zip
```

## 開発

```bash
# テスト実行
dotnet test

# フォーマット
dotnet format
```

## ドキュメント

詳細な設計については [DESIGN.md](./DESIGN.md) を参照してください。

## ライセンス

[MIT License](./LICENSE)

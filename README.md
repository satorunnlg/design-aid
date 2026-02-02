# Design Aid (DA)

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

- `da check` - ファイルハッシュの整合性検証
- `da verify` - 設計基準に基づくバリデーション
- `da sync` - ファイルシステムと DB の同期
- `da deploy` - 手配パッケージの作成
- `da search` - 類似設計のベクトル検索
- `da init` - 新規プロジェクトの初期化
- `da status` - プロジェクト状態の表示

## 技術スタック

- **言語**: C# 12+ / .NET 8.0+
- **CLI**: System.CommandLine
- **ORM**: Entity Framework Core (SQLite)
- **Vector DB**: Qdrant

## セットアップ

### 前提条件

- .NET 8.0 SDK
- Docker (Qdrant 用)

### インストール

```bash
# リポジトリのクローン
git clone https://github.com/your-org/design-aid.git
cd design-aid

# 依存関係の復元
dotnet restore

# Qdrant の起動
docker compose up -d

# ビルド
dotnet build
```

### 実行

```bash
# ヘルプ表示
dotnet run --project src/DesignAid -- --help

# プロジェクト初期化
dotnet run --project src/DesignAid -- init

# 整合性チェック
dotnet run --project src/DesignAid -- check
```

## ドキュメント

詳細な設計については [DESIGN.md](./DESIGN.md) を参照してください。

## ライセンス

[MIT License](./LICENSE)

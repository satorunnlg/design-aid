# DESIGN.md - Design Aid (DA) プロジェクト設計書

## 概要

### プロジェクト名

Design Aid (DA)

### 目的

機械設計における「設計の論理（理）」と「物理的な手配」の乖離を埋めるためのサポートシステム。
AWS CDK の思想を機械設計に適用し、**手配境界**を抽象化の基準として、設計の整合性と知見の継承を支援する。

### 設計哲学

| 原則 | 説明 |
|------|------|
| **Support, Not Control** | 設計者を縛るのではなく、ミスの検知や過去の知見の提示を通じて「助ける」存在 |
| **Procurement Boundary** | 自社から他社へ手配（発注）するタイミングをシステムの境界とする |
| **Hash-Based Integrity** | 全ての成果物（図面・計算書）をハッシュ値で管理し、不整合を根絶する |

### 対象ユーザー

- 機械設計者（製図・選定・手配を行う技術者）
- 設計部門のリーダー（設計資産の継承・ナレッジ管理）
- 調達・購買部門（手配状況の可視化）

## 技術スタック

| カテゴリ | 技術 | バージョン | 備考 |
|---------|------|-----------|------|
| 言語 | C# | 12+ | |
| フレームワーク | .NET | 8.0+ | |
| CLI フレームワーク | System.CommandLine | 2.x | サブコマンド構造 |
| ORM | Entity Framework Core | 8.x | SQLite 連携 |
| ローカル DB | SQLite | | EF Core 経由 |
| Vector DB | Qdrant | 1.x | Docker で起動 |
| Qdrant Client | Qdrant.Client | | NuGet パッケージ |
| テスト | xUnit | | |
| フォーマッター | dotnet format | | |
| 将来GUI | Avalonia UI | 11.x | 将来対応 |

## アーキテクチャ

### システム構成図

```
┌──────────────────────────────────────────────────────────────────┐
│                         Design Aid (DA)                          │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                      CLI Layer                                │ │
│  │   daid check │ daid verify │ daid sync │ daid deploy │ daid search │ │
│  └─────────────────────────┬───────────────────────────────────┘ │
│                            │                                      │
│  ┌─────────────────────────▼───────────────────────────────────┐ │
│  │                   Application Layer                          │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐ │ │
│  │  │ HashService  │ │ SyncService  │ │ ValidationService    │ │ │
│  │  │  (整合性検証) │ │ (DB同期)     │ │ (設計基準バリデーション) │ │ │
│  │  └──────────────┘ └──────────────┘ └──────────────────────┘ │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐ │ │
│  │  │ DeployService│ │ SearchService│ │ PartService          │ │ │
│  │  │  (手配出力)   │ │ (類似検索)   │ │ (パーツ管理)          │ │ │
│  │  └──────────────┘ └──────────────┘ └──────────────────────┘ │ │
│  └─────────────────────────┬───────────────────────────────────┘ │
│                            │                                      │
│  ┌─────────────────────────▼───────────────────────────────────┐ │
│  │                   Infrastructure Layer                       │ │
│  │  ┌─────────────────┐            ┌─────────────────────────┐ │ │
│  │  │    SQLite       │            │        Qdrant           │ │ │
│  │  │  (design_aid.db)│            │   (Vector Search)       │ │ │
│  │  │                 │            │                         │ │ │
│  │  │ - Parts         │            │ - design_knowledge      │ │ │
│  │  │ - Handover      │            │   (仕様・パラメータ)      │ │ │
│  │  │ - Standards     │            │                         │ │ │
│  │  └─────────────────┘            └─────────────────────────┘ │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                   File System (部品境界)                     │ │
│  │  /project/components/SP-2026-PLATE-01/                       │ │
│  │    ├── part.json      (パーツ定義)                           │ │
│  │    ├── drawing.dxf    (製作図面)                             │ │
│  │    └── selection.pdf  (選定根拠)                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### レイヤー構成

| レイヤー | 責務 | 依存方向 |
|---------|------|---------|
| CLI | コマンド解析、入出力、ユーザーインタラクション | → Application |
| Application | ビジネスロジック、サービス | → Domain, Infrastructure |
| Domain | エンティティ、値オブジェクト、ドメインルール | なし（最下層） |
| Infrastructure | DB アクセス、外部サービス連携 | → Domain |

## ディレクトリ構造

```
design-aid/
├── src/
│   └── DesignAid/
│       ├── Commands/                      # CLI コマンド定義
│       │   ├── Asset/                     # 装置管理
│       │   │   ├── AssetAddCommand.cs
│       │   │   ├── AssetListCommand.cs
│       │   │   ├── AssetRemoveCommand.cs
│       │   │   └── AssetLinkCommand.cs    # 子装置組み込み
│       │   ├── Part/                      # パーツ管理
│       │   │   ├── PartAddCommand.cs
│       │   │   ├── PartListCommand.cs
│       │   │   └── PartLinkCommand.cs     # 装置へパーツ紐づけ
│       │   ├── CheckCommand.cs            # daid check
│       │   ├── VerifyCommand.cs           # daid verify
│       │   ├── SyncCommand.cs             # daid sync
│       │   ├── DeployCommand.cs           # daid deploy
│       │   ├── SearchCommand.cs           # daid search
│       │   ├── StatusCommand.cs           # daid status
│       │   ├── BackupCommand.cs           # daid backup
│       │   └── UpdateCommand.cs           # daid update
│       ├── Domain/                        # ドメインモデル
│       │   ├── Entities/
│       │   │   ├── Asset.cs               # 装置
│       │   │   ├── DesignComponent.cs     # パーツ基底クラス
│       │   │   ├── FabricatedPart.cs      # 製作物
│       │   │   ├── PurchasedPart.cs       # 購入品
│       │   │   ├── StandardPart.cs        # 規格品
│       │   │   ├── DesignStandard.cs      # 設計基準
│       │   │   └── HandoverRecord.cs      # 手配履歴
│       │   ├── ValueObjects/
│       │   │   ├── PartNumber.cs          # 型式（人間が識別する番号）
│       │   │   ├── FileHash.cs            # ファイルハッシュ
│       │   │   ├── HandoverStatus.cs      # 手配ステータス
│       │   │   └── ValidationResult.cs    # バリデーション結果
│       │   └── Standards/                 # 設計基準インターフェース
│       │       ├── IDesignStandard.cs     # 基準インターフェース
│       │       ├── MaterialStandard.cs    # 材料基準
│       │       └── ToleranceStandard.cs   # 公差基準
│       ├── Application/                   # アプリケーションサービス
│       │   ├── Services/
│       │   │   ├── AssetService.cs        # 装置管理
│       │   │   ├── PartService.cs         # パーツ管理
│       │   │   ├── HashService.cs         # ハッシュ計算・検証
│       │   │   ├── SyncService.cs         # DB同期
│       │   │   ├── ValidationService.cs   # 設計基準バリデーション
│       │   │   ├── DeployService.cs       # 手配パッケージ作成
│       │   │   └── SearchService.cs       # 類似設計検索
│       │   └── DTOs/
│       │       ├── AssetDto.cs
│       │       ├── PartDto.cs
│       │       ├── CheckResultDto.cs
│       │       └── SearchResultDto.cs
│       ├── Infrastructure/                # インフラストラクチャ
│       │   ├── Persistence/
│       │   │   ├── DesignAidDbContext.cs  # EF Core DbContext
│       │   │   ├── Configurations/        # エンティティ設定
│       │   │   │   ├── AssetConfiguration.cs
│       │   │   │   ├── PartConfiguration.cs
│       │   │   │   └── HandoverConfiguration.cs
│       │   │   └── Migrations/            # マイグレーション
│       │   ├── Qdrant/
│       │   │   ├── QdrantService.cs       # Qdrant クライアント
│       │   │   └── EmbeddingService.cs    # ベクトル化サービス
│       │   └── FileSystem/
│       │       ├── AssetJsonReader.cs     # asset.json 読み書き
│       │       ├── PartJsonReader.cs      # part.json 読み書き
│       │       └── ArtifactScanner.cs     # 成果物スキャン
│       ├── Configuration/                 # 設定
│       │   ├── AppSettings.cs
│       │   └── DependencyInjection.cs     # DI 設定
│       ├── Program.cs                     # エントリーポイント
│       └── DesignAid.csproj
├── tests/
│   └── DesignAid.Tests/
│       ├── Domain/
│       │   ├── AssetTests.cs
│       │   ├── DesignComponentTests.cs
│       │   └── FileHashTests.cs
│       ├── Application/
│       │   ├── AssetServiceTests.cs
│       │   ├── HashServiceTests.cs
│       │   ├── ValidationServiceTests.cs
│       │   └── SyncServiceTests.cs
│       ├── Integration/
│       │   ├── QdrantIntegrationTests.cs
│       │   └── SqliteIntegrationTests.cs
│       └── DesignAid.Tests.csproj
├── data/                                  # 開発用データディレクトリ
│   ├── config.json                        # 開発用設定
│   ├── design_aid.db                      # 開発用DB（gitignore）
│   ├── assets/                            # 装置
│   │   └── sample-asset/
│   │       └── asset.json
│   └── components/                        # 部品
│       └── SP-2026-PLATE-01/
│           └── part.json
├── docker-compose.yml                     # Qdrant 起動用
├── appsettings.json                       # 設定ファイル
├── appsettings.Development.json           # 開発用設定（DA_DATA_DIR=./data）
├── DesignAid.sln
├── CLAUDE.md
└── DESIGN.md
```

## プロジェクト初期化

### ソリューション作成

```bash
# ソリューション作成
dotnet new sln -n DesignAid

# メインプロジェクト作成
dotnet new console -n DesignAid -o src/DesignAid

# テストプロジェクト作成
dotnet new xunit -n DesignAid.Tests -o tests/DesignAid.Tests

# ソリューションに追加
dotnet sln add src/DesignAid/DesignAid.csproj
dotnet sln add tests/DesignAid.Tests/DesignAid.Tests.csproj

# テストプロジェクトから本体を参照
dotnet add tests/DesignAid.Tests reference src/DesignAid
```

### 必須パッケージ

```bash
# CLI フレームワーク
dotnet add src/DesignAid package System.CommandLine

# Entity Framework Core + SQLite
dotnet add src/DesignAid package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/DesignAid package Microsoft.EntityFrameworkCore.Design

# Qdrant クライアント
dotnet add src/DesignAid package Qdrant.Client

# JSON シリアライズ
dotnet add src/DesignAid package System.Text.Json

# テスト用
dotnet add tests/DesignAid.Tests package Microsoft.EntityFrameworkCore.InMemory
dotnet add tests/DesignAid.Tests package Moq
```

## 開発コマンド

### 環境構築

```bash
# 依存関係の復元
dotnet restore

# 開発用データディレクトリ作成
mkdir -p data/projects

# 環境変数設定（開発用）
# Windows (PowerShell)
$env:DA_DATA_DIR = "./data"

# Windows (cmd)
set DA_DATA_DIR=./data

# Linux/macOS
export DA_DATA_DIR="./data"

# Qdrant 起動
docker compose up -d

# DB マイグレーション（初回）
dotnet ef database update --project src/DesignAid
```

### ビルド

```bash
dotnet build
```

### テスト

```bash
dotnet test
```

### 実行

```bash
# ヘルプ表示
dotnet run --project src/DesignAid -- --help

# 各コマンド実行
dotnet run --project src/DesignAid -- check
dotnet run --project src/DesignAid -- verify
dotnet run --project src/DesignAid -- sync
dotnet run --project src/DesignAid -- deploy
dotnet run --project src/DesignAid -- search "キーワード"
```

### フォーマット

```bash
dotnet format
```

### 発行

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained

# 単一実行ファイル
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## データ構造

### システムディレクトリ

Design Aid はシステムディレクトリに設定・DB・装置・部品を一元管理する。
「物」がトップに来る設計思想に基づき、Asset（装置）と Component（部品）を中心に構成。

```text
# 本番環境
~/.design-aid/                      # Windows: %APPDATA%\design-aid
├── config.json                     # グローバル設定
├── design_aid.db                   # 統合DB
├── assets/                         # 装置（トップレベル）
│   └── ...
└── components/                     # 部品（共有リソース）
    └── ...

# 開発環境（このリポジトリ内）
design-aid/
├── src/
├── tests/
├── data/                           # 開発用データ
│   ├── config.json
│   ├── design_aid.db
│   ├── assets/                     # 装置
│   │   └── lifting-unit/
│   │       └── asset.json
│   └── components/                 # 部品
│       └── SP-2026-PLATE-01/
│           └── part.json
└── ...
```

### 階層構造

```
装置 (Asset) ※トップレベル
  ├── 部品 (Component) ※複数（中間テーブルで紐づけ）
  └── 子装置 (SubAsset) ※別のAssetを組み込み可能

部品 (Component) ※共有リソース
  - 複数の装置から参照可能
  - データディレクトリに一元管理
```

| 階層 | 説明 | 例 |
|------|------|-----|
| Asset | 装置・ユニット（トップレベル） | `lifting-unit`, `control-panel`, `conveyor-A` |
| Component | 手配境界となる部品（共有可能） | `SP-2026-PLATE-01`, `MTR-001` |
| SubAsset | 別の装置の組み込み（再利用） | `safety-module`（既存Assetを参照） |

**特徴:**
- Asset は Component（部品）を持つ
- Asset は別の Asset を SubAsset として組み込み可能（過去の装置の再利用）
- Component は複数の Asset から共有参照可能

### データディレクトリ構造

装置（Asset）と部品（Component）はデータディレクトリに一元管理する。

```text
data/
├── config.json                     # 設定ファイル
├── design_aid.db                   # SQLite DB
├── assets/                         # 装置（トップレベル）
│   ├── lifting-unit/
│   │   └── asset.json              # 装置定義
│   ├── control-panel/
│   │   └── asset.json
│   └── safety-module/              # 他の装置から組み込み可能
│       └── asset.json
└── components/                     # 部品（共有リソース）
    ├── SP-2026-PLATE-01/
    │   ├── part.json               # パーツ定義
    │   ├── drawing.dxf             # 製作図面
    │   └── calculation.pdf         # 計算書
    └── MTR-001/
        ├── part.json
        └── spec.pdf                # 選定根拠
```

### asset.json 仕様

```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "name": "lifting-unit",
  "display_name": "昇降ユニット",
  "description": "エレベータ更新案件の昇降機構",
  "created_at": "2026-02-02T10:30:00Z"
}
```

**注意**: 部品との紐づけは DB の中間テーブル（AssetComponents）で管理。
子装置との紐づけは DB の中間テーブル（AssetSubAssets）で管理。

### 手配境界（Procurement Boundary）

本システムでは、以下のディレクトリ構造を「1つの部品（パーツ）」の最小単位として扱う。
部品はデータディレクトリの `components/` に一元管理される。

```text
data/components/SP-2026-PLATE-01/
  ├── part.json      # パーツ定義（手動/自動生成）
  ├── drawing.dxf    # 製作図面
  └── selection.pdf  # 選定根拠/計算書
```

### part.json 仕様

**注意**: `asset_id` は持たない。装置との紐づけは DB の中間テーブル（AssetComponents）で管理。

```json
{
  "id": "770e8400-e29b-41d4-a716-446655440002",
  "part_number": "SP-2026-PLATE-01",
  "name": "昇降ベースプレート",
  "type": "Fabricated",
  "version": "1.1.0",
  "artifacts": [
    { "path": "drawing.dxf", "hash": "sha256:abc123..." },
    { "path": "selection.pdf", "hash": "sha256:def456..." }
  ],
  "standards": ["STD-MATERIAL-01", "STD-TOLERANCE-02"],
  "metadata": {
    "material": "SS400",
    "surface_treatment": "メッキ",
    "lead_time_days": 14
  },
  "memo": "前回のプロジェクトより剛性を20%強化"
}
```

### ID と型式の使い分け

| フィールド | 形式 | 用途 | 例 |
|-----------|------|------|-----|
| `id` | UUID v4 | 内部データ管理、DB主キー、一意識別 | `550e8400-e29b-41d4-a716-446655440000` |
| `part_number` | 英数字記号 | 人間が識別する型式、図面番号 | `SP-2026-PLATE-01`, `ABC-123_REV2` |

- `id` は自動生成（`daid init` 時に UUID を発行）
- `part_number` はユーザーが任意に設定（必須、ユニーク制約あり）
- CLI 出力では `part_number` を表示（`--show-id` で UUID も表示）

### パーツタイプ

| タイプ | 説明 | 例 |
|--------|------|-----|
| `Fabricated` | 製作物（図面品） | 加工部品、溶接構造物 |
| `Purchased` | 購入品 | モーター、センサー、バルブ |
| `Standard` | 規格品 | ボルト、ナット、ベアリング |

## SQLite スキーマ

### テーブル定義

```sql
-- 装置マスタ（トップレベル）
CREATE TABLE Assets (
    Id TEXT PRIMARY KEY,          -- UUID v4
    Name TEXT NOT NULL UNIQUE,    -- 装置名（ディレクトリ名、グローバルユニーク）
    DisplayName TEXT,             -- 表示名
    Description TEXT,
    DirectoryPath TEXT NOT NULL,  -- data/assets/xxx への相対パス
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IX_Assets_Name ON Assets(Name);

-- パーツマスタ（共有リソース）
CREATE TABLE Parts (
    Id TEXT PRIMARY KEY,          -- UUID v4
    PartNumber TEXT NOT NULL UNIQUE, -- 型式（グローバルユニーク）
    Name TEXT NOT NULL,
    Type TEXT NOT NULL,           -- Fabricated/Purchased/Standard
    Version TEXT NOT NULL,
    CurrentHash TEXT NOT NULL,    -- 最新の成果物ハッシュ（結合）
    DirectoryPath TEXT NOT NULL,  -- data/components/xxx への相対パス
    MetaDataJson TEXT,            -- JSON 形式のメタデータ
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IX_Parts_PartNumber ON Parts(PartNumber);
CREATE INDEX IX_Parts_Type ON Parts(Type);

-- 装置-部品 中間テーブル（多対多）
CREATE TABLE AssetComponents (
    AssetId TEXT NOT NULL,        -- Assets.Id への参照
    PartId TEXT NOT NULL,         -- Parts.Id への参照
    Quantity INTEGER DEFAULT 1,   -- 使用数量
    Notes TEXT,                   -- 備考（この装置での用途など）
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY (AssetId, PartId),
    FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE,
    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE RESTRICT
);

CREATE INDEX IX_AssetComponents_AssetId ON AssetComponents(AssetId);
CREATE INDEX IX_AssetComponents_PartId ON AssetComponents(PartId);

-- 装置-子装置 中間テーブル（Asset の入れ子・再利用）
CREATE TABLE AssetSubAssets (
    ParentAssetId TEXT NOT NULL,  -- 親装置の Assets.Id
    ChildAssetId TEXT NOT NULL,   -- 子装置の Assets.Id（組み込まれる装置）
    Quantity INTEGER DEFAULT 1,   -- 使用数量
    Notes TEXT,                   -- 備考（この装置での用途など）
    CreatedAt TEXT NOT NULL,
    PRIMARY KEY (ParentAssetId, ChildAssetId),
    FOREIGN KEY (ParentAssetId) REFERENCES Assets(Id) ON DELETE CASCADE,
    FOREIGN KEY (ChildAssetId) REFERENCES Assets(Id) ON DELETE RESTRICT,
    CHECK (ParentAssetId != ChildAssetId)  -- 自己参照禁止
);

CREATE INDEX IX_AssetSubAssets_ParentAssetId ON AssetSubAssets(ParentAssetId);
CREATE INDEX IX_AssetSubAssets_ChildAssetId ON AssetSubAssets(ChildAssetId);

-- 手配履歴
CREATE TABLE HandoverHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId TEXT NOT NULL,         -- Parts.Id への参照
    CommittedHash TEXT NOT NULL,  -- 手配時のハッシュ
    Status TEXT NOT NULL,         -- Ordered/Delivered/Canceled
    OrderDate TEXT NOT NULL,
    DeliveryDate TEXT,
    Notes TEXT,
    FOREIGN KEY (PartId) REFERENCES Parts(Id)
);

CREATE INDEX IX_HandoverHistory_PartId ON HandoverHistory(PartId);
CREATE INDEX IX_HandoverHistory_Status ON HandoverHistory(Status);

-- 設計基準リンク
CREATE TABLE StandardLinks (
    PartId TEXT NOT NULL,         -- Parts.Id への参照
    StandardId TEXT NOT NULL,
    PRIMARY KEY (PartId, StandardId),
    FOREIGN KEY (PartId) REFERENCES Parts(Id)
);

-- 設計基準マスタ
CREATE TABLE Standards (
    StandardId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    ValidationRuleJson TEXT       -- JSON 形式のバリデーションルール
);
```

### 手配ステータス

| ステータス | 説明 |
|-----------|------|
| `Draft` | 設計中（未手配） |
| `Ordered` | 手配済み |
| `Delivered` | 納品済み |
| `Canceled` | キャンセル |

## マイグレーション戦略

### 基本方針

- **開発中**: EF Core マイグレーションを使用
- **破壊的変更**: データ移行スクリプトを作成
- **ロールバック**: 各マイグレーションに Down メソッドを実装

### マイグレーションコマンド

```bash
# マイグレーション作成
dotnet ef migrations add <MigrationName> --project src/DesignAid

# DB 更新
dotnet ef database update --project src/DesignAid

# 特定バージョンへロールバック
dotnet ef database update <PreviousMigrationName> --project src/DesignAid

# マイグレーション一覧
dotnet ef migrations list --project src/DesignAid

# SQL スクリプト生成（レビュー用）
dotnet ef migrations script --project src/DesignAid -o migration.sql
```

### マイグレーションルール

1. **命名規則**: `YYYYMMDD_説明` 形式（例: `20260202_AddPartNumberIndex`）
2. **1マイグレーション1変更**: 小さな単位で作成し、追跡を容易に
3. **データ保持**: カラム削除前に必ずデータ移行を検討
4. **テスト**: マイグレーション後に `daid check` で整合性確認

### 破壊的変更の手順

```csharp
// 例: カラム名変更 (PartId -> Id)
public partial class RenamePartIdToId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. 新カラム追加
        migrationBuilder.AddColumn<string>("Id", "Parts", nullable: true);

        // 2. データ移行
        migrationBuilder.Sql("UPDATE Parts SET Id = PartId");

        // 3. NOT NULL 制約追加
        migrationBuilder.AlterColumn<string>("Id", "Parts", nullable: false);

        // 4. 旧カラム削除
        migrationBuilder.DropColumn("PartId", "Parts");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 逆の手順でロールバック
        migrationBuilder.AddColumn<string>("PartId", "Parts", nullable: true);
        migrationBuilder.Sql("UPDATE Parts SET PartId = Id");
        migrationBuilder.AlterColumn<string>("PartId", "Parts", nullable: false);
        migrationBuilder.DropColumn("Id", "Parts");
    }
}
```

### Qdrant データの再同期

スキーマ変更後、Qdrant のベクトルデータも更新が必要な場合:

```bash
# ベクトルデータを再生成
daid sync --include-vectors --force

# コレクションを再作成（破壊的）
daid sync --recreate-collection
```

### バックアップ戦略

```bash
# SQLite バックアップ（マイグレーション前に推奨）
cp design_aid.db design_aid.db.backup.$(date +%Y%m%d_%H%M%S)

# Qdrant スナップショット（Docker ボリューム）
docker exec da-qdrant /qdrant/qdrant snapshot create design_knowledge
```

## Qdrant 設計

### コレクション設計

```
Collection: design_knowledge
├── id: UUID
├── vector: float[N]     # 埋め込みベクトル（次元数はプロバイダーに依存）
└── payload:
    ├── part_id: UUID            # パーツ内部ID
    ├── part_number: string      # 型式
    ├── asset_id: UUID           # 装置内部ID
    ├── asset_name: string       # 装置名
    ├── project_id: UUID         # プロジェクト内部ID
    ├── project_name: string     # プロジェクト名
    ├── type: string (spec/memo/parameter)
    ├── content: string (元テキスト)
    ├── file_path: string
    └── created_at: string
```

### ベクトル化対象

| 対象 | 説明 |
|------|------|
| 仕様テキスト | part.json の memo、metadata |
| 計算書内容 | PDF/Excel から抽出したテキスト |
| 図面注記 | DXF/DWG から抽出した注記 |

## CLI コマンド仕様

### コマンド体系

```
daid <command> [subcommand] [options]

# 装置管理
daid asset add <name>             # 装置を追加
daid asset list                   # 装置一覧
daid asset remove <name>          # 装置を削除
daid asset link <parent> <child>  # 子装置を組み込み（SubAsset）

# パーツ管理
daid part add <part-number>       # パーツを追加
daid part list                    # パーツ一覧
daid part link <asset> <part>     # 装置にパーツを紐づけ

# 整合性・検証
daid check                        # ハッシュ整合性チェック
daid verify                       # 設計基準バリデーション
daid sync                         # DB同期

# 手配
daid deploy                       # 手配パッケージ作成

# 検索
daid search <query>               # 類似設計検索

# 状態確認
daid status                       # システム状態表示

# バックアップ
daid backup                       # データバックアップ（S3/ZIP）

# ツール管理
daid update                       # ツールを最新版に更新
```

### daid asset add

装置を追加する。

```bash
# 装置を追加
daid asset add lifting-unit

# 表示名を指定
daid asset add lifting-unit --display-name "昇降ユニット"

# 説明を指定
daid asset add lifting-unit --description "エレベータ更新案件の昇降機構"
```

**出力例:**
```
Asset created: lifting-unit
  Path: data/assets/lifting-unit
  ID: 660e8400-e29b-41d4-a716-446655440001
```

### daid asset list

登録済み装置を一覧表示する。

```bash
daid asset list

# JSON 出力
daid asset list --json
```

**出力例:**
```
Assets:

  lifting-unit (昇降ユニット)
    ID: 660e8400-e29b-41d4-a716-446655440001
    Parts: 15
    SubAssets: 1

  control-panel (制御盤)
    ID: 660e8400-e29b-41d4-a716-446655440002
    Parts: 20
    SubAssets: 0

Total: 2 assets, 35 parts
```

### daid asset link

別の装置を子装置として組み込む（SubAsset）。

```bash
# lifting-unit に safety-module を組み込む
daid asset link lifting-unit safety-module

# 数量と備考を指定
daid asset link lifting-unit safety-module --quantity 2 --notes "冗長構成"
```

**出力例:**
```
SubAsset linked: safety-module -> lifting-unit
  Quantity: 2
  Notes: 冗長構成
```

### daid part add

装置にパーツを追加する。

```bash
# カレント装置にパーツを追加
daid part add SP-2026-PLATE-01 --type Fabricated --name "昇降ベースプレート"

# 装置を指定
daid part add SP-2026-PLATE-01 --asset lifting-unit --type Fabricated

# メタデータ付き
daid part add SP-2026-PLATE-01 --type Fabricated --material SS400
```

**出力例:**
```
Part created: SP-2026-PLATE-01
  Asset: lifting-unit
  Type: Fabricated
  Path: .../components/SP-2026-PLATE-01/

Created:
  - part.json
```

### daid check

ファイルハッシュの整合性を検証する。

```bash
# カレントディレクトリをチェック
daid check

# 特定パスをチェック
daid check --path /path/to/project

# JSON 出力
daid check --json
```

**出力例:**
```
Checking design integrity...

[OK] SP-2026-PLATE-01
    drawing.dxf: Hash matches
    selection.pdf: Hash matches

[WARNING] SP-2026-MOTOR-01
    spec.pdf: Hash mismatch (file modified after last sync)
    Expected: sha256:abc123...
    Actual:   sha256:def456...

[ERROR] SP-2026-BRACKET-01
    drawing.dxf: File not found

Summary: 1 OK, 1 Warning, 1 Error
```

### daid verify

設計基準に基づくバリデーションを実行する。

```bash
# 全パーツを検証
daid verify

# 特定パーツを検証
daid verify --part SP-2026-PLATE-01

# 特定基準のみ検証
daid verify --standard STD-MATERIAL-01
```

**出力例:**
```
Verifying against design standards...

[PASS] SP-2026-PLATE-01
    STD-MATERIAL-01: Material SS400 is approved
    STD-TOLERANCE-02: Tolerance within spec

[FAIL] SP-2026-BRACKET-01
    STD-MATERIAL-01: Material A5052 requires approval for structural use
    Recommendation: Use SS400 or obtain engineering approval

Summary: 1 Pass, 1 Fail
```

### daid sync

ファイルシステムと SQLite/Qdrant を同期する。

```bash
# 同期実行
daid sync

# ドライラン（変更確認のみ）
daid sync --dry-run

# Qdrant への同期も含む
daid sync --include-vectors
```

**出力例:**
```
Syncing design data...

[NEW] SP-2026-SHAFT-01
    Added to database

[UPDATED] SP-2026-PLATE-01
    Hash updated: sha256:old... -> sha256:new...

[DELETED] SP-2026-OLD-PART-01
    Marked as deleted (file removed)

Sync complete: 1 new, 1 updated, 1 deleted
```

### daid deploy

手配パッケージを作成する。

```bash
# 変更があるパーツの手配パッケージを作成
daid deploy

# 特定パーツのみ
daid deploy --part SP-2026-PLATE-01

# 出力先指定
daid deploy --output /path/to/output
```

**出力例:**
```
Creating deployment package...

Parts to deploy:
  - SP-2026-PLATE-01 (modified since last order)
  - SP-2026-BRACKET-01 (new)

Output: ./deploy_2026-02-02/
├── SP-2026-PLATE-01/
│   ├── drawing.dxf
│   ├── selection.pdf
│   └── part_info.txt
├── SP-2026-BRACKET-01/
│   └── ...
└── manifest.json

Mark these parts as ordered? [y/N]
```

### daid search

類似設計をベクトル検索する。

```bash
# キーワード検索
daid search "昇降機構 SS400"

# 類似度閾値指定
daid search "ベアリング選定" --threshold 0.8

# 上位N件
daid search "モーター" --top 5
```

**出力例:**
```
Searching similar designs...

Query: "昇降機構 SS400"

Results:
1. [0.92] SP-2024-LIFT-BASE-01 (Project: elevator-renewal)
   "昇降ユニット ベースフレーム SS400 溶接構造"

2. [0.87] SP-2023-PLATFORM-01 (Project: production-line)
   "昇降テーブル プラットフォーム SS400"

3. [0.82] SP-2025-SLIDE-01 (Project: packaging-machine)
   "スライド機構 ベースプレート SS400"

Found 3 similar designs
```

### daid init（非推奨）

`daid project add --create` を使用してください。

### daid status

システム全体またはプロジェクトの状態を表示する。

```bash
# システム全体
daid status

# 特定プロジェクト
daid status --project elevator-renewal

# 特定装置
daid status --project elevator-renewal --asset lifting-unit
```

**出力例（システム全体）:**
```
Design Aid Status

System:
  Database: ~/.design-aid/design_aid.db
  Qdrant: Connected (localhost:6333)

Projects: 2
  elevator-renewal     3 assets, 45 parts
  packaging-line-2026  5 assets, 120 parts

Total: 165 parts (Draft: 15, Ordered: 130, Delivered: 20)
```

**出力例（プロジェクト指定）:**
```
Project: elevator-renewal
  Path: C:/work/elevator-renewal
  Last sync: 2026-02-02 10:30:00

Assets:
  lifting-unit     (15 parts)
  control-panel    (20 parts)
  safety-system    (10 parts)

Parts Summary:
  Total: 45
  Draft: 5
  Ordered: 35
  Delivered: 5

Recent Changes:
  [lifting-unit] SP-2026-PLATE-01: Modified 2 hours ago (not synced)
  [control-panel] PLC-001: Modified 1 day ago (synced)
```

## ドメインモデル

### DesignComponent（基底クラス）

```csharp
namespace DesignAid.Domain.Entities;

/// <summary>
/// 全てのパーツの基底クラス。
/// 手配境界を越えるために必要な最小限の情報を保持する。
/// </summary>
public abstract class DesignComponent
{
    /// <summary>内部ID（UUID v4、データ管理用）</summary>
    public Guid Id { get; protected set; }

    /// <summary>型式（人間が識別する番号、例: SP-2026-PLATE-01）</summary>
    public PartNumber PartNumber { get; protected set; }

    /// <summary>パーツ名</summary>
    public string Name { get; protected set; }

    /// <summary>バージョン（セマンティックバージョニング）</summary>
    public string Version { get; protected set; }

    /// <summary>成果物ファイルパスとハッシュ値のマップ</summary>
    public Dictionary<string, FileHash> ArtifactHashes { get; protected set; } = new();

    /// <summary>手配ステータス</summary>
    public HandoverStatus Status { get; protected set; }

    /// <summary>
    /// 成果物のハッシュを計算し、整合性を検証する。
    /// </summary>
    public abstract ValidationResult ValidateIntegrity();
}
```

### PartNumber（値オブジェクト）

```csharp
namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// 型式を表す値オブジェクト。
/// 英数字、ハイフン、アンダースコア、ピリオドを許可。
/// </summary>
public readonly record struct PartNumber
{
    private static readonly Regex ValidPattern = new(
        @"^[A-Za-z0-9\-_.]+$",
        RegexOptions.Compiled);

    public string Value { get; }

    public PartNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("型式は必須です", nameof(value));

        if (!ValidPattern.IsMatch(value))
            throw new ArgumentException(
                "型式には英数字、ハイフン、アンダースコア、ピリオドのみ使用可能です",
                nameof(value));

        Value = value;
    }

    public override string ToString() => Value;

    public static implicit operator string(PartNumber pn) => pn.Value;
}
```

### FabricatedPart（製作物）

```csharp
namespace DesignAid.Domain.Entities;

/// <summary>
/// 製作物（図面品）。自社が設計責任を持ち、図面を正とする。
/// </summary>
public class FabricatedPart : DesignComponent
{
    /// <summary>材質</summary>
    public string Material { get; set; }

    /// <summary>表面処理</summary>
    public string SurfaceTreatment { get; set; }

    /// <summary>図面パス</summary>
    public string DrawingPath { get; set; }

    /// <summary>加工リードタイム（日）</summary>
    public int LeadTimeDays { get; set; }

    public override ValidationResult ValidateIntegrity()
    {
        // 図面ファイルの存在確認とハッシュ検証
        // ...
    }
}
```

### IDesignStandard（設計基準インターフェース）

```csharp
namespace DesignAid.Domain.Standards;

/// <summary>
/// 設計基準（理）を定義するインターフェース。
/// </summary>
public interface IDesignStandard
{
    /// <summary>基準ID</summary>
    string StandardId { get; }

    /// <summary>基準名</summary>
    string Name { get; }

    /// <summary>
    /// パーツに対してバリデーションを実行する。
    /// </summary>
    ValidationResult Validate(DesignComponent component);
}
```

## 設定項目

### システムディレクトリ解決

| 環境 | パス | 備考 |
|------|------|------|
| 本番 (Windows) | `%APPDATA%\design-aid\` | 例: `C:\Users\<user>\AppData\Roaming\design-aid\` |
| 本番 (Linux/macOS) | `~/.design-aid/` | |
| 開発 | `<repo>/data/` | 環境変数 `DA_DATA_DIR` で上書き可能 |

### appsettings.json

```json
{
  "DesignAid": {
    "SystemDirectory": null,
    "Database": {
      "Path": "design_aid.db"
    },
    "Qdrant": {
      "Host": "localhost",
      "Port": 6333,
      "CollectionName": "design_knowledge",
      "Enabled": true
    },
    "Embedding": {
      "Provider": "OpenAI",
      "Providers": {
        "OpenAI": {
          "Model": "text-embedding-3-small",
          "Dimensions": 1536
        },
        "Ollama": {
          "Host": "http://localhost:11434",
          "Model": "nomic-embed-text",
          "Dimensions": 768
        },
        "Azure": {
          "Endpoint": "${DA_AZURE_ENDPOINT}",
          "DeploymentName": "${DA_AZURE_DEPLOYMENT}",
          "Dimensions": 1536
        }
      }
    },
    "Hashing": {
      "Algorithm": "SHA256"
    }
  }
}
```

**注意**: `SystemDirectory` が `null` の場合、OS に応じたデフォルトパスを使用。
開発時は環境変数 `DA_DATA_DIR` で `./data` を指定することを推奨。

### Embedding プロバイダー設計

```csharp
namespace DesignAid.Infrastructure.Embedding;

/// <summary>
/// 埋め込みベクトル生成のインターフェース
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>プロバイダー名</summary>
    string Name { get; }

    /// <summary>ベクトル次元数</summary>
    int Dimensions { get; }

    /// <summary>テキストをベクトル化</summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>複数テキストを一括ベクトル化</summary>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct = default);
}

// DI 登録例
services.AddKeyedScoped<IEmbeddingProvider, OpenAiEmbeddingProvider>("OpenAI");
services.AddKeyedScoped<IEmbeddingProvider, OllamaEmbeddingProvider>("Ollama");
services.AddKeyedScoped<IEmbeddingProvider, AzureEmbeddingProvider>("Azure");

// 使用時はファクトリーパターンで切り替え
public class EmbeddingProviderFactory
{
    public IEmbeddingProvider Create(string providerName)
    {
        // 設定に基づいてプロバイダーを返す
    }
}
```

### 環境変数

| 変数名 | 必須 | デフォルト | 説明 |
|--------|------|-----------|------|
| `DA_DATA_DIR` | - | OS依存 | システムディレクトリ（DB、設定の配置先） |
| `DA_EMBEDDING_PROVIDER` | - | `OpenAI` | 使用する埋め込みプロバイダー |
| `DA_EMBEDDING_API_KEY` | △ | - | 埋め込み API キー（プロバイダーによる） |
| `DA_DB_PATH` | - | `./design_aid.db` | SQLite DBパス |
| `DA_QDRANT_HOST` | - | `localhost` | Qdrant ホスト |
| `DA_QDRANT_PORT` | - | `6333` | Qdrant ポート |
| `DA_QDRANT_ENABLED` | - | `true` | Qdrant 機能の有効/無効 |
| `DA_AZURE_ENDPOINT` | △ | - | Azure OpenAI エンドポイント |
| `DA_AZURE_DEPLOYMENT` | △ | - | Azure OpenAI デプロイメント名 |

※ △ = プロバイダー選択時に必須

### シークレット管理

開発段階では環境変数で管理する。

```bash
# Windows (PowerShell)
$env:DA_EMBEDDING_API_KEY = "sk-..."

# Windows (cmd)
set DA_EMBEDDING_API_KEY=sk-...

# Linux/macOS
export DA_EMBEDDING_API_KEY="sk-..."
```

**注意事項:**
- API キーは appsettings.json に直接記載しない
- `.env` ファイルを使用する場合は `.gitignore` に追加すること
- 本番環境ではシークレット管理サービス（Azure Key Vault 等）への移行を推奨

## Docker Compose 設定

### docker-compose.yml

```yaml
version: '3.8'

services:
  qdrant:
    image: qdrant/qdrant:latest
    container_name: da-qdrant
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_storage:/qdrant/storage
    environment:
      - QDRANT__SERVICE__GRPC_PORT=6334

volumes:
  qdrant_storage:
```

## テスト戦略

### テスト分類

| 分類 | 対象 | フレームワーク | 優先度 |
|------|------|--------------|--------|
| ユニットテスト | ドメインモデル、サービス | xUnit | 最優先 |
| 統合テスト | SQLite 連携、Qdrant 連携 | xUnit + TestContainers | 高 |
| E2E テスト | CLI コマンド全体 | xUnit | 中 |

### テスト例

```csharp
public class HashServiceTests
{
    [Fact]
    public void ComputeHash_ValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var service = new HashService();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test content");

        // Act
        var hash = service.ComputeHash(tempFile);

        // Assert
        Assert.StartsWith("sha256:", hash.Value);
        Assert.Equal(71, hash.Value.Length); // sha256: + 64 hex chars

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public void ValidateIntegrity_HashMismatch_ReturnsWarning()
    {
        // Arrange
        var part = new FabricatedPart
        {
            PartId = new PartId("TEST-001"),
            ArtifactHashes = new Dictionary<string, FileHash>
            {
                { "drawing.dxf", new FileHash("sha256:expected...") }
            }
        };
        var service = new HashService();

        // Act
        var result = service.ValidateIntegrity(part, "sha256:actual...");

        // Assert
        Assert.Equal(ValidationSeverity.Warning, result.Severity);
    }
}
```

## 開発フロー

### 推奨実装順序

#### フェーズ1: 基盤構築
1. プロジェクトスケルトン作成
2. ドメインモデル実装（DesignComponent, PartId, FileHash）
3. part.json の読み書き
4. ハッシュ計算サービス

#### フェーズ2: ローカルDB
1. Entity Framework Core セットアップ
2. SQLite マイグレーション
3. パーツ CRUD 操作
4. `daid init`, `daid status` コマンド

#### フェーズ3: コア機能
1. `daid check` コマンド（ハッシュ検証）
2. `daid sync` コマンド（DB同期）
3. 手配履歴管理
4. `daid deploy` コマンド

#### フェーズ4: 知見検索
1. Qdrant 連携
2. ベクトル化サービス
3. `daid search` コマンド
4. 類似設計の推薦

#### フェーズ5: 設計基準
1. IDesignStandard インターフェース
2. 材料基準、公差基準の実装
3. `daid verify` コマンド
4. カスタム基準の追加機能

#### フェーズ6: 将来拡張（GUI）
1. Avalonia UI プロジェクト追加
2. GUI_POLICY.md 作成
3. ダッシュボード画面
4. パーツ一覧・検索画面

## エラーハンドリング方針

### 終了コード

| コード | 意味 | 使用場面 |
|--------|------|---------|
| 0 | 成功 | 正常終了 |
| 1 | 一般エラー | 実行時エラー、予期しないエラー |
| 2 | 引数エラー | 不正なコマンドライン引数 |
| 3 | 設定エラー | 設定ファイル不正、環境変数未設定 |
| 4 | 接続エラー | Qdrant/外部サービスへの接続失敗 |
| 5 | 整合性エラー | ハッシュ不整合、データ破損検知 |

### 例外処理戦略

```csharp
// カスタム例外階層
namespace DesignAid.Domain.Exceptions;

/// <summary>
/// Design Aid の基底例外クラス
/// </summary>
public abstract class DesignAidException : Exception
{
    public int ExitCode { get; }

    protected DesignAidException(string message, int exitCode, Exception? inner = null)
        : base(message, inner)
    {
        ExitCode = exitCode;
    }
}

/// <summary>設定関連のエラー</summary>
public class ConfigurationException : DesignAidException
{
    public ConfigurationException(string message, Exception? inner = null)
        : base(message, exitCode: 3, inner) { }
}

/// <summary>外部サービス接続エラー</summary>
public class ConnectionException : DesignAidException
{
    public ConnectionException(string service, string message, Exception? inner = null)
        : base($"{service}: {message}", exitCode: 4, inner) { }
}

/// <summary>整合性検証エラー</summary>
public class IntegrityException : DesignAidException
{
    public IntegrityException(string message, Exception? inner = null)
        : base(message, exitCode: 5, inner) { }
}
```

### ValidationResult 構造

```csharp
namespace DesignAid.Domain.ValueObjects;

/// <summary>
/// バリデーション結果を表す値オブジェクト
/// </summary>
public record ValidationResult
{
    public ValidationSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Target { get; init; }
    public IReadOnlyList<ValidationDetail> Details { get; init; } = [];

    public bool IsSuccess => Severity == ValidationSeverity.Ok;
    public bool HasWarnings => Severity == ValidationSeverity.Warning;
    public bool HasErrors => Severity == ValidationSeverity.Error;

    public static ValidationResult Ok(string? target = null)
        => new() { Severity = ValidationSeverity.Ok, Target = target };

    public static ValidationResult Warning(string message, string? target = null)
        => new() { Severity = ValidationSeverity.Warning, Message = message, Target = target };

    public static ValidationResult Error(string message, string? target = null)
        => new() { Severity = ValidationSeverity.Error, Message = message, Target = target };
}

public enum ValidationSeverity
{
    Ok,
    Warning,
    Error
}

public record ValidationDetail(string Field, string Message, ValidationSeverity Severity);
```

### 出力フォーマット

```
# 通常出力（stderr）
[ERROR] SP-2026-PLATE-01: ファイルが見つかりません
  詳細: drawing.dxf が存在しません
  対処: ファイルを配置するか、part.json を更新してください

# --verbose オプション時（stderr）
[ERROR] SP-2026-PLATE-01: ファイルが見つかりません
  詳細: drawing.dxf が存在しません
  パス: C:\projects\my-project\components\SP-2026-PLATE-01\drawing.dxf
  対処: ファイルを配置するか、part.json を更新してください
  スタックトレース:
    at DesignAid.Application.Services.HashService.ComputeHash(...)
    ...

# --json オプション時（stdout）
{
  "success": false,
  "exitCode": 5,
  "errors": [
    {
      "code": "FILE_NOT_FOUND",
      "target": "SP-2026-PLATE-01",
      "message": "ファイルが見つかりません",
      "details": {
        "file": "drawing.dxf",
        "expectedPath": "C:\\projects\\..."
      }
    }
  ]
}
```

### グレースフルデグラデーション

| 状況 | 動作 |
|------|------|
| Qdrant 未接続 | 警告を出して検索機能を無効化、他の機能は継続 |
| part.json 不正 | 該当パーツをスキップし、他のパーツは処理継続 |
| ハッシュ不整合 | 警告として報告、処理は継続（`--strict` で中断） |

## コーディング規約

### C#

- Microsoft C# コーディング規則に従う
- nullable 参照型を有効化
- コメントは日本語で記述

### 名前空間

```
DesignAid                           # ルート
DesignAid.Commands                  # CLI コマンド
DesignAid.Domain                    # ドメイン層
DesignAid.Domain.Entities           # エンティティ
DesignAid.Domain.ValueObjects       # 値オブジェクト
DesignAid.Domain.Standards          # 設計基準
DesignAid.Application               # アプリケーション層
DesignAid.Application.Services      # サービス
DesignAid.Application.DTOs          # DTO
DesignAid.Infrastructure            # インフラ層
DesignAid.Infrastructure.Persistence # DB 永続化
DesignAid.Infrastructure.Qdrant     # Qdrant 連携
DesignAid.Infrastructure.FileSystem # ファイルシステム
DesignAid.Configuration             # 設定
```

## 変更履歴

| 日付 | バージョン | 変更内容 | 担当 |
|------|-----------|---------|------|
| | 0.1.0 | 初版作成 | - |

## 備考

### 未設定項目（開発開始前に要設定）

- [ ] 埋め込みプロバイダーの選定（OpenAI / Ollama / Azure）
- [ ] 設計基準の具体的ルール定義
- [ ] 材料データベースの準備

### 将来拡張予定

- [ ] GUI（Avalonia UI）の追加
- [ ] CAD 連携（DXF/DWG 直接読み込み）
- [ ] Excel 帳票自動生成
- [ ] 3D CAD 対応（STEP/IGES）
- [ ] チーム共有機能（サーバー版）

### 関連ドキュメント

将来 GUI 追加時:
- [GUI_POLICY.md](./GUI_POLICY.md) - GUI設計ポリシー（GUI追加時に作成）

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
| 言語 | C# | 13 | |
| フレームワーク | .NET | 10.0 | |
| CLI フレームワーク | System.CommandLine | 2.x | サブコマンド構造 |
| ORM | Entity Framework Core | 10.x | SQLite 連携 |
| ローカル DB | SQLite | | EF Core 経由 |
| ベクトル検索 | HNSW (curiosity-ai) | 26.x | インプロセス ANN 検索。NuGet `HNSW` |
| Web UI | Blazor Server | (ASP.NET Core 同梱) | `daid dashboard` で起動 |
| UI コンポーネント | MudBlazor | 8.x | マテリアルデザイン |
| テスト | xUnit | | |
| フォーマッター | dotnet format | | |
| 将来GUI | Avalonia UI | 11.x | 将来対応（比較評価予定） |

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
│  │  ┌───────────────────────────────────────────────────┐      │ │
│  │  │    SQLite (design_aid.db)                          │      │ │
│  │  │                                                    │      │ │
│  │  │ - Parts / Handover / Standards (リレーショナル)      │      │ │
│  │  │ - VectorIndex (ベクトル BLOB + メタデータ)           │      │ │
│  │  └───────────────────────────────────────────────────┘      │ │
│  │                                                              │ │
│  │  ┌───────────────────────────────────────────────────┐      │ │
│  │  │    HNSW (インメモリ ANN インデックス)                │      │ │
│  │  │    - sync 時に構築、ファイルにシリアライズ            │      │ │
│  │  │    - search 時にデシリアライズして利用               │      │ │
│  │  └───────────────────────────────────────────────────┘      │ │
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
| Dashboard (Blazor) | Web ダッシュボード、データ可視化 | → Application |
| Application | ビジネスロジック、サービス（インターフェース経由） | → Domain, Infrastructure |
| Domain | エンティティ、値オブジェクト、ドメインルール | なし（最下層） |
| Infrastructure | DB アクセス、外部サービス連携 | → Domain |

> **DI 基盤**: `ServiceCollectionExtensions.AddDesignAidServices()` で全サービスを DI コンテナに登録。
> CLI・Dashboard・将来の Avalonia UI で Application Layer を共有する設計。

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
│       │   ├── Dashboard/                 # ダッシュボード
│       │   │   ├── DashboardCommand.cs   # daid dashboard
│       │   │   └── DashboardStopCommand.cs # daid dashboard stop
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
│       │   │   ├── SearchService.cs       # 類似設計検索
│       │   │   └── Interfaces/           # サービスインターフェース（DI 用）
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
│       │   ├── VectorSearch/
│       │   │   └── VectorSearchService.cs # ベクトル検索サービス（SQLite+HNSW）
│       │   └── FileSystem/
│       │       ├── AssetJsonReader.cs     # asset.json 読み書き
│       │       ├── PartJsonReader.cs      # part.json 読み書き
│       │       └── ArtifactScanner.cs     # 成果物スキャン
│       ├── Dashboard/                     # Web ダッシュボード（Blazor Server）
│       │   ├── Components/
│       │   │   ├── Layout/               # MainLayout, NavMenu
│       │   │   └── Pages/                # Home, Parts, Assets, Check, Search
│       │   ├── Services/
│       │   │   └── DashboardService.cs   # ダッシュボード専用サービス
│       │   ├── App.razor                 # ルートコンポーネント
│       │   ├── Routes.razor
│       │   └── _Imports.razor
│       ├── wwwroot/                       # 静的ファイル（CSS）
│       ├── Configuration/                 # 設定
│       │   ├── AppSettings.cs
│       │   ├── DependencyInjection.cs
│       │   └── ServiceCollectionExtensions.cs # DI 登録拡張メソッド
│       ├── Program.cs                     # エントリーポイント
│       └── DesignAid.csproj               # SDK: Microsoft.NET.Sdk.Web
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
│       │   ├── VectorSearchIntegrationTests.cs
│       │   └── SqliteIntegrationTests.cs
│       └── DesignAid.Tests.csproj
├── .test-integration/                     # 統合テスト用ディレクトリ（gitignore）
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

# HNSW ベクトル検索（インプロセス ANN）
dotnet add src/DesignAid package HNSW

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

# プロジェクトディレクトリの初期化（名前付き）
daid setup my-project
cd my-project

# または、既存ディレクトリをプロジェクトとして初期化（名前なし）
mkdir my-project && cd my-project
daid setup

# 環境変数（オプション: 明示的にプロジェクトルートを指定する場合）
# Windows (PowerShell)
$env:DA_DATA_DIR = "C:/path/to/project"

# Linux/macOS
export DA_DATA_DIR="/path/to/project"
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

# 単一実行ファイル（トリミング付き）
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
```

> **注意**: トリミング付きパブリッシュでは `TrimMode=partial` と EF Core 向け `TrimmerRootAssembly` が
> csproj で設定済み。これにより EF Core のリフレクション型が保持される。

## データ構造

### システムディレクトリ

Design Aid はシステムディレクトリに設定・DB・装置・部品を一元管理する。
「物」がトップに来る設計思想に基づき、Asset（装置）と Component（部品）を中心に構成。

```text
# 本番環境
~/.design-aid/                      # Windows: %APPDATA%\design-aid
├── design_aid.db                   # 統合DB（リレーショナル + ベクトル BLOB + Settings テーブル）
├── hnsw_index.bin                  # HNSW グラフキャッシュ（再構築可能）
├── assets/                         # 装置（トップレベル）
│   └── ...
└── components/                     # 部品（共有リソース）
    └── ...

# 開発環境（このリポジトリ内）
design-aid/
├── src/
├── tests/
├── data/                           # 開発用データ
│   ├── design_aid.db              # 設定は Settings テーブルに格納
│   ├── hnsw_index.bin              # HNSW グラフキャッシュ（gitignore）
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
  - プロジェクトディレクトリに一元管理
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

### プロジェクトディレクトリ構造

装置（Asset）と部品（Component）はプロジェクトディレクトリに一元管理する。
プロジェクトルートは `design_aid.db` の存在で識別される。

```text
<project-root>/                     # daid setup で初期化されたディレクトリ
├── design_aid.db                   # SQLite DB（Settings テーブルに設定を格納）
├── .gitignore                      # DB やインデックスファイルを除外
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
部品はプロジェクトルートの `components/` に一元管理される。

```text
<project-root>/components/SP-2026-PLATE-01/
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

-- ベクトルインデックス（類似検索用）
CREATE TABLE VectorIndex (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId TEXT NOT NULL,            -- Parts.Id への参照
    PartNumber TEXT NOT NULL,        -- 検索結果表示用（キャッシュ）
    Content TEXT NOT NULL,           -- ベクトル化対象テキスト
    Embedding BLOB NOT NULL,         -- float[] をバイト列で保存
    Dimensions INTEGER NOT NULL,     -- ベクトル次元数
    HnswInternalId INTEGER,          -- HNSW グラフ内の内部ID（sync 時に付与）
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE
);

CREATE INDEX IX_VectorIndex_PartId ON VectorIndex(PartId);
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

### ベクトルインデックスの再構築

スキーマ変更後、ベクトルデータの更新が必要な場合:

```bash
# ベクトルデータを再生成（全パーツの埋め込みを再計算し HNSW を再構築）
daid sync --include-vectors --force
```

### バックアップ戦略

```bash
# SQLite バックアップ（マイグレーション前に推奨）
# DB にベクトルも含まれるため、これだけで全データをバックアップ可能
cp design_aid.db design_aid.db.backup.$(date +%Y%m%d_%H%M%S)

# HNSW キャッシュは再構築可能なためバックアップ不要
```

## ベクトル検索設計

### アーキテクチャ概要

Qdrant（外部ベクトル DB）に代わり、**SQLite + HNSW ライブラリ**によるインプロセスベクトル検索を採用。
Docker 不要で単一実行ファイルとして配布可能。

```
┌─────────────────────────────────────────────────────────────┐
│                    VectorSearchService                       │
│                                                              │
│  ┌──────────────────────┐    ┌────────────────────────────┐ │
│  │ SQLite VectorIndex   │    │ HNSW SmallWorld<float[],f> │ │
│  │                      │    │                            │ │
│  │ - Embedding (BLOB)   │◄──►│ - ANN インデックス          │ │
│  │ - PartId, Content    │    │ - Cosine 距離 (SIMD)       │ │
│  │ - メタデータ          │    │ - シリアライズ/復元         │ │
│  └──────────────────────┘    └────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────┐                                    │
│  │ IEmbeddingProvider   │  ← Mock / OpenAI / Ollama / Azure │
│  └──────────────────────┘                                    │
└─────────────────────────────────────────────────────────────┘
```

### データフロー

```
【同期時: daid sync --include-vectors】
  パーツ (Parts テーブル + part.json)
    → コンテンツ構築 (名前, 型, メモ, メタデータ)
    → IEmbeddingProvider で float[] 生成
    → SQLite VectorIndex に INSERT (BLOB)
    → HNSW インデックスを全件から構築
    → hnsw_index.bin にシリアライズ (グラフキャッシュ)

【検索時: daid search】
  クエリテキスト
    → IEmbeddingProvider で float[] 生成
    → SQLite から全ベクトル読み込み
    → hnsw_index.bin からグラフ復元 (存在すれば)
    → HNSW KNNSearch で上位k件取得
    → コサイン距離 → 類似度スコアに変換 (score = 1 - distance)
    → 閾値フィルタ → 結果返却
```

### HNSW パラメータ

| パラメータ | 値 | 説明 |
|-----------|-----|------|
| M | 16 | ゼロ層以上の最大近傍数 |
| LevelLambda | 1/ln(16) | レベル分布パラメータ |
| ConstructionPruning | 200 | 構築時の候補数 (efConstruction) |
| EfSearch | 100 | 検索時の候補数 |
| NeighbourHeuristic | SelectHeuristic | Algorithm 4 使用 |
| 距離関数 | CosineDistance.SIMDForUnits | SIMD 加速コサイン距離 |

### float[] ⇔ BLOB 変換

```csharp
// float[] → byte[]（SQLite 保存用）
public static byte[] ToBlob(float[] vector)
{
    var bytes = new byte[vector.Length * sizeof(float)];
    Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
    return bytes;
}

// byte[] → float[]（SQLite 読み出し後の復元）
public static float[] FromBlob(byte[] blob)
{
    var vector = new float[blob.Length / sizeof(float)];
    Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
    return vector;
}
```

### HNSW インデックスの永続化

HNSW ライブラリはグラフ構造のみをシリアライズする（ベクトルデータは含まない）。
復元時にはベクトルデータを別途渡す必要がある。

```
保存対象:
  hnsw_index.bin  → HNSW グラフ構造（MessagePack 形式）
  VectorIndex テーブル → ベクトル BLOB + メタデータ（SQLite、ソースオブトゥルース）

復元フロー:
  1. SQLite VectorIndex から全ベクトルを読み込み → List<float[]>
  2. hnsw_index.bin から SmallWorld.DeserializeGraph() でグラフ復元
  3. 検索可能な状態に
  ※ hnsw_index.bin が存在しない場合は SQLite のベクトルから再構築
```

### 削除の扱い

HNSW ライブラリはベクトル削除をサポートしない。以下の方針で対処:

- `daid part remove` → Parts テーブルから DELETE（CASCADE で VectorIndex も削除）
- `daid sync --include-vectors` → VectorIndex を全クリアし全パーツから再構築
- CLI ツールの特性上、sync が明示的な再構築ポイントであり問題なし

### ベクトル化対象

| 対象 | 説明 |
|------|------|
| 仕様テキスト | part.json の memo、metadata |
| 計算書内容 | PDF/Excel から抽出したテキスト |
| 図面注記 | DXF/DWG から抽出した注記 |

### メモリ使用量の目安

| パーツ数 | 次元 | ベクトル | グラフ | 合計 |
|---------|------|---------|-------|------|
| 1,000 | 768 | 3 MB | 0.3 MB | ~3 MB |
| 10,000 | 768 | 30 MB | 2.5 MB | ~33 MB |
| 10,000 | 1536 | 60 MB | 2.5 MB | ~63 MB |

CLI ツールのため、`daid search` 実行時のみメモリにロードし、プロセス終了で解放。

## CLI コマンド仕様

### コマンド体系

```
daid <command> [subcommand] [options]

# 装置管理
daid asset add <name>                          # 装置を追加（git init 付き）
daid asset add <name> --no-git                 # 装置を追加（git init なし）
daid asset list                                # 装置一覧
daid asset list --verbose                      # 装置一覧（詳細表示）
daid asset remove <name>                       # 装置を削除
daid asset link <parent> --child <child>       # 子装置を組み込み（SubAsset）
daid asset unlink <parent> --child <child>     # 子装置リンクを解除

# パーツ管理
daid part add <part-number>                    # パーツを追加（git init 付き）
daid part add <part-number> --no-git           # パーツを追加（git init なし）
daid part list                                 # パーツ一覧
daid part link <part-number> --asset <asset>   # 装置にパーツを紐づけ
daid part remove <part-number>                 # パーツを削除

# 整合性・検証
daid check                        # ハッシュ整合性チェック
daid verify                       # 設計基準バリデーション
daid sync                         # DB同期

# 手配
daid deploy                       # 手配パッケージ作成
daid deploy --dry-run             # 手配パッケージ確認（ドライラン）

# 検索
daid search <query>               # 類似設計検索

# 状態確認
daid status                       # システム状態表示

# バックアップ
daid backup                       # データバックアップ（S3/ZIP）

# アーカイブ（容量節約）
daid archive asset <name>              # 装置をアーカイブ
daid archive part <part-number>        # パーツをアーカイブ
daid archive list                      # アーカイブ一覧を表示
daid archive restore asset <name>      # 装置をアーカイブから復元
daid archive restore part <part-number> # パーツをアーカイブから復元

# ツール管理
daid update                       # ツールを最新版に更新
```

### daid asset add

装置を追加する。デフォルトで `git init` を実行し、装置ディレクトリを Git リポジトリとして初期化する。

```bash
# 装置を追加（git init 付き）
daid asset add lifting-unit

# 表示名を指定
daid asset add lifting-unit --display-name "昇降ユニット"

# 説明を指定
daid asset add lifting-unit --description "エレベータ更新案件の昇降機構"

# Git リポジトリを初期化しない
daid asset add lifting-unit --no-git
```

**出力例:**
```
Asset created: lifting-unit
  Path: data/assets/lifting-unit
  ID: 660e8400-e29b-41d4-a716-446655440001
  Git: initialized
```

### daid asset list

登録済み装置を一覧表示する。

```bash
daid asset list

# 詳細表示（紐付けパーツ・子装置を表示）
daid asset list --verbose

# JSON 出力
daid asset list --json
```

**出力例:**
```
Assets:

  lifting-unit (昇降ユニット)

  control-panel (制御盤)

  safety-module (安全モジュール)

Total: 3 assets
```

**出力例（--verbose）:**
```
Assets:

  lifting-unit (昇降ユニット)
    Linked Parts:
      - BASE-PLATE-001 (x1)
      - MTR-001 (x2)
    Child Assets:
      - safety-module (x1)

  control-panel (制御盤)
    Linked Parts: (none)
    Child Assets: (none)

Total: 2 assets
```

### daid asset link

別の装置を子装置として組み込む（SubAsset）。

```bash
# lifting-unit に safety-module を組み込む
daid asset link lifting-unit --child safety-module

# 数量と備考を指定
daid asset link lifting-unit --child safety-module --quantity 2 --notes "冗長構成"
```

**出力例:**
```
Child asset linked: safety-module to lifting-unit
  Quantity: 2
  Notes: 冗長構成
```

### daid asset unlink

子装置のリンクを解除する。

```bash
# lifting-unit から safety-module を解除
daid asset unlink lifting-unit --child safety-module
```

**出力例:**
```
Child asset unlinked: safety-module from lifting-unit
```

### daid part add

パーツを追加する。デフォルトで `git init` を実行し、パーツディレクトリを Git リポジトリとして初期化する。

```bash
# パーツを追加（git init 付き）
daid part add SP-2026-PLATE-01 --type Fabricated --name "昇降ベースプレート"

# Git リポジトリを初期化しない
daid part add SP-2026-PLATE-01 --type Fabricated --name "ベースプレート" --no-git

# メタデータ付き
daid part add SP-2026-PLATE-01 --type Fabricated --name "ベースプレート" --material SS400
```

**出力例:**
```
Part created: SP-2026-PLATE-01
  Name: 昇降ベースプレート
  Type: Fabricated
  Path: data/components/SP-2026-PLATE-01
  ID: 770e8400-e29b-41d4-a716-446655440002
  Git: initialized
```

### daid part link

パーツを装置に紐づける。

```bash
# パーツを装置にリンク
daid part link SP-2026-PLATE-01 --asset lifting-unit

# 数量を指定
daid part link SP-2026-PLATE-01 --asset lifting-unit --quantity 2
```

**出力例:**
```
Part linked: SP-2026-PLATE-01 to lifting-unit
  Quantity: 2
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

ファイルシステムと SQLite を同期する。`--include-vectors` でベクトルインデックスも再構築する。

```bash
# 同期実行
daid sync

# ドライラン（変更確認のみ）
daid sync --dry-run

# ベクトルインデックスの再構築も含む
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

# ドライラン（確認のみ、実際のデプロイは行わない）
daid deploy --dry-run

# 特定パーツのみ
daid deploy --part SP-2026-PLATE-01

# 出力先指定
daid deploy --output /path/to/output
```

**出力例（--dry-run）:**
```
[DRY-RUN] 実際のデプロイは行われません

Deploy candidates:
  - SP-2026-PLATE-01 (Fabricated) - 成果物: 2ファイル
  - MTR-001 (Purchased) - 成果物: 1ファイル

Total: 2 parts ready for deploy
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

### daid status

システム全体の状態を表示する。

```bash
# システム全体
daid status
```

**出力例:**
```
Design Aid Status

System:
  Database: ~/.design-aid/design_aid.db
  Vector Index: 40 vectors (768 dimensions)

Assets: 3
  lifting-unit     (15 parts)
  control-panel    (20 parts)
  safety-module    (5 parts)

Parts: 40 total
  Fabricated: 25
  Purchased: 10
  Standard: 5
```

### daid archive

装置やパーツをアーカイブして容量を節約する。アーカイブされたデータは ZIP 圧縮され、ベクトルインデックスは維持されるため検索は引き続き可能。

```bash
# 装置をアーカイブ
daid archive asset old-unit

# パーツをアーカイブ
daid archive part OLD-PART-001

# アーカイブ一覧を表示
daid archive list

# JSON 形式で出力
daid archive list --json

# アーカイブから復元
daid archive restore asset old-unit
daid archive restore part OLD-PART-001
```

**出力例（アーカイブ時）:**
```
Archiving asset: old-unit
  Source: data/assets/old-unit
  Target: data/archive/assets/old-unit.zip

Asset archived: old-unit
  Original size: 15.2 MB
  Archive size: 3.8 MB
  Saved: 75.0%

Note: Vector index entries are preserved for search.
```

**出力例（一覧表示）:**
```
Archived items:

Assets:
  old-unit (旧昇降ユニット)
    Archived: 2026-02-05 10:30
    Size: 3.8 MB (saved 11.4 MB)

Parts:
  OLD-PART-001 (旧ベースプレート)
    Archived: 2026-02-05 10:35
    Size: 512.0 KB (saved 1.5 MB)

Total: 2 items, 4.3 MB (saved 12.9 MB)
```

**データ構造:**
```text
data/
├── assets/                        # アクティブな装置
├── components/                    # アクティブなパーツ
├── archive/                       # アーカイブ領域
│   ├── assets/
│   │   └── old-unit.zip           # 圧縮された装置
│   └── components/
│       └── OLD-PART-001.zip       # 圧縮されたパーツ
└── archive_index.json             # アーカイブ管理インデックス
```

### `daid dashboard` — Web ダッシュボード

```bash
daid dashboard [--port 5180] [--no-browser]
daid dashboard stop
```

**機能:**
- Blazor Server ベースの Web ダッシュボードを起動する
- フォアグラウンドで Kestrel サーバーを起動し、ブラウザを自動オープン
- PID ファイル (`data/.dashboard.pid`) で重複起動を防止
- Ctrl+C または `daid dashboard stop` で停止

**画面構成:**

| パス | 画面名 | 主要機能 |
|------|--------|---------|
| `/` | ダッシュボード | 装置数・パーツ数・ステータス集計・最近の更新 |
| `/parts` | パーツ一覧 | フィルター（種別・ステータス・テキスト検索） |
| `/assets` | 装置一覧 | カード形式一覧、詳細表示（パーツ展開） |
| `/check` | 整合性チェック | ハッシュ整合性チェック実行・結果表示 |
| `/search` | 類似検索 | ベクトル検索（要 `daid sync --include-vectors`） |

**オプション:**

| オプション | デフォルト | 説明 |
|-----------|----------|------|
| `--port` | 5180 | 起動ポート番号 |
| `--no-browser` | false | ブラウザ自動オープンを無効化 |

**停止方法:**
- `daid dashboard stop`: PID ファイルからポートを取得し `/api/shutdown` に POST
- `Ctrl+C`: Graceful Shutdown（PID ファイルを自動削除）

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

### 設定アーキテクチャ

全ての設定は SQLite の **Settings テーブル**に一元管理される。
ブートストラップ（DB の場所特定）のみ環境変数を使用する。

- **ブートストラップ**: `DA_DATA_DIR` 環境変数 or `design_aid.db` の上方探索（最大2階層）
- **DB ファイル名**: `design_aid.db` 固定
- **設定の読み書き**: `daid config show` / `daid config set <key> <value>`
- **旧 config.json**: `daid setup` 時に自動的に Settings テーブルへ移行

### Settings テーブル

```sql
CREATE TABLE Settings (
    Key TEXT PRIMARY KEY,    -- dot-notation キー
    Value TEXT NOT NULL,     -- 設定値（文字列）
    UpdatedAt TEXT NOT NULL  -- 最終更新日時（ISO 8601）
);
```

### デフォルト設定値

| Key | Default | 説明 |
|-----|---------|------|
| `database.path` | `design_aid.db` | DB ファイル名（相対パス） |
| `vector_search.enabled` | `true` | ベクトル検索有効/無効 |
| `vector_search.hnsw_index_path` | `hnsw_index.bin` | HNSW インデックスファイルパス |
| `embedding.provider` | `Mock` | 埋め込みプロバイダー名 |
| `embedding.dimensions` | `384` | ベクトル次元数 |
| `embedding.model` | (null) | モデル名 |
| `embedding.api_key` | (null) | API キー |
| `embedding.endpoint` | (null) | エンドポイント URL |
| `hashing.algorithm` | `SHA256` | ハッシュアルゴリズム |
| `backup.s3_bucket` | (空文字) | S3 バケット名 |
| `backup.s3_prefix` | `design-aid-backup/` | S3 プレフィックス |
| `backup.aws_profile` | `default` | AWS CLI プロファイル |

### プロジェクトルート解決

コマンド実行時、以下の優先順でプロジェクトルート（`design_aid.db` があるディレクトリ）を探索する。

| 優先度 | 方法 | 備考 |
|--------|------|------|
| 1 | 環境変数 `DA_DATA_DIR` | 明示的に指定（後方互換） |
| 2 | カレントディレクトリに `design_aid.db` | 深さ 0 |
| 3 | 1 階層上に `design_aid.db` | 深さ 1（assets/ や components/ 内から実行時） |
| 4 | 2 階層上に `design_aid.db` | 深さ 2（assets/xxx/ や components/xxx/ 内から実行時） |

見つからない場合はエラー（exit code 3）:
```
[ERROR] プロジェクトが見つかりません。
  カレントディレクトリから上方向に design_aid.db を探しましたが見つかりませんでした。
  対処: プロジェクトディレクトリ内で実行するか、`daid setup` で初期化してください。
```

#### setup コマンドの動作

| コマンド | 動作 |
|---------|------|
| `daid setup` | カレントディレクトリをプロジェクトルートとして初期化 |
| `daid setup <name>` | `<name>/` ディレクトリを作成し、その中を初期化 |
| `daid setup --force` | 既存の初期化済みディレクトリを再初期化 |

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
```

プロバイダーの切り替えは `daid config set embedding.provider <name>` で行う。
API キーも `daid config set embedding.api_key <key>` で DB に保存される。

## テスト戦略

### テスト分類

| 分類 | 対象 | フレームワーク | 優先度 |
|------|------|--------------|--------|
| ユニットテスト | ドメインモデル、サービス | xUnit | 最優先 |
| 統合テスト | SQLite 連携、ベクトル検索連携 | xUnit | 高 |
| CLI 統合テスト | CLI コマンド全体 | スクリプト (PowerShell/bash) | 高 |

### テスト関連ドキュメント・スクリプト

| ファイル | 用途 |
|---------|------|
| [docs/TEST_SCENARIO.md](./docs/TEST_SCENARIO.md) | CLI コマンドテストシナリオ（手動・自動共通） |
| [scripts/test-all.ps1](./scripts/test-all.ps1) | CLI 統合テストスクリプト（Windows PowerShell） |
| [scripts/test-all.sh](./scripts/test-all.sh) | CLI 統合テストスクリプト（Linux/Mac bash） |
| [scripts/Run-Tests.ps1](./scripts/Run-Tests.ps1) | ユニットテスト実行スクリプト |
| [scripts/Setup-TestData.ps1](./scripts/Setup-TestData.ps1) | ユニットテスト用サンプルデータ作成 |

### コマンド追加時のテスト要件（CRITICAL）

**新しいコマンドを追加する際は、必ず以下の手順を実施すること:**

1. **テストシナリオの検討・追加**
   - `docs/TEST_SCENARIO.md` に新コマンドのテストシナリオを追加
   - 正常系・異常系・境界値を網羅

2. **テストスクリプトの更新**
   - `scripts/test-all.ps1` に新コマンドのテストを追加
   - `scripts/test-all.sh` に新コマンドのテストを追加

3. **テストの実施・確認**
   - テストスクリプトを実行し、全テストがパスすることを確認
   - 既存テストへの影響がないことを確認

```bash
# テストの実行方法

# ユニットテスト
dotnet test

# CLI 統合テスト（Windows）
.\scripts\test-all.ps1

# CLI 統合テスト（Linux/Mac）
./scripts/test-all.sh
```

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
1. VectorSearchService 実装（SQLite + HNSW）
2. 埋め込みプロバイダー連携
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
| 4 | 接続エラー | 外部サービスへの接続失敗 |
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
| ベクトルインデックス未構築 | 警告を出してキーワード検索にフォールバック、他の機能は継続 |
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
DesignAid.Infrastructure.VectorSearch # ベクトル検索（SQLite+HNSW）
DesignAid.Infrastructure.FileSystem # ファイルシステム
DesignAid.Configuration             # 設定
```

## 変更履歴

| 日付 | バージョン | 変更内容 | 担当 |
|------|-----------|---------|------|
| | 0.1.0 | 初版作成 | - |
| 2026-02-15 | - | ベクトル検索を Qdrant から SQLite+HNSW に移行（Docker 依存解消） | - |

## 備考

### 未設定項目（開発開始前に要設定）

- [ ] 埋め込みプロバイダーの選定（OpenAI / Ollama / Azure）
- [ ] 設計基準の具体的ルール定義
- [ ] 材料データベースの準備

### 将来拡張予定

- [ ] MCP サーバー対応（`daid mcp` - Claude Desktop 等との連携）→ 下記「MCP サーバー設計検討」参照
- [ ] GUI（Avalonia UI）の追加
- [ ] CAD 連携（DXF/DWG 直接読み込み）
- [ ] Excel 帳票自動生成
- [ ] 3D CAD 対応（STEP/IGES）
- [ ] チーム共有機能（サーバー版）

### MCP サーバー設計検討（2026-02-06 調査）

**ステータス**: 実装見送り（C# SDK 安定版リリース待ち）

#### MCP (Model Context Protocol) とは

Anthropic が公開したオープンプロトコル。AI アプリケーション（Claude Desktop、VS Code Copilot 等）と
外部ツール・データソースを標準化された方法で接続する仕組み。JSON-RPC 2.0 ベース。
Microsoft、OpenAI、Google DeepMind 等も採用しており、事実上の業界標準。

#### 技術選定

| 項目 | 選定 | 理由 |
|------|------|------|
| SDK | `ModelContextProtocol` (NuGet) | Microsoft + Anthropic 共同メンテナンスの公式 C# SDK |
| トランスポート | **STDIO** | CLI ツールとの親和性が最も高い。Claude Desktop / VS Code が直接プロセス起動 |
| 起動方法 | `daid mcp` サブコマンド | 既存の System.CommandLine 体系と統合 |

**SDK 状況（2026-02-06 時点）**:
- 最新: `ModelContextProtocol 0.7.0-preview.1`（プレビュー版）
- 破壊的変更のリスクあり（v0.5.0 で大規模リファクタリング実施済み）
- 安定版（1.0）リリース後に実装開始を推奨

#### 公開予定の MCP ツール

| MCP ツール | 対応 CLI コマンド | 説明 |
|-----------|-----------------|------|
| `ListParts` | `daid part list` | パーツ一覧を取得 |
| `GetPartDetails` | part.json 読み取り | パーツの詳細情報を返却 |
| `CheckIntegrity` | `daid check` | ハッシュ整合性をチェック |
| `SearchDesigns` | `daid search` | 類似設計をベクトル検索 |
| `VerifyStandards` | `daid verify` | 設計基準バリデーション |
| `GetAssetParts` | `daid asset list --verbose` | 装置に紐づくパーツ情報を取得 |
| `GetStatus` | `daid status` | システム状態を取得 |

#### 実装構成（予定）

```
src/DesignAid/
├── Commands/
│   └── McpCommand.cs             # daid mcp サブコマンド（MCP サーバー起動）
├── Mcp/                          # MCP サーバー関連
│   ├── DesignAidMcpTools.cs      # ツール定義（[McpServerToolType]）
│   ├── DesignAidMcpResources.cs  # リソース定義（パーツ情報等）
│   └── DesignAidMcpPrompts.cs    # プロンプト定義（設計レビュー等）
```

**追加パッケージ**:
```bash
dotnet add src/DesignAid package ModelContextProtocol --prerelease
```

※ `Microsoft.Extensions.Hosting` は既存の DI 構成で対応済み

#### 起動・設定例

```bash
# MCP サーバーモードで起動
daid mcp
```

**Claude Desktop (`claude_desktop_config.json`)**:
```json
{
  "mcpServers": {
    "design-aid": {
      "command": "daid",
      "args": ["mcp"]
    }
  }
}
```

**VS Code (`.vscode/mcp.json`)**:
```json
{
  "servers": {
    "design-aid": {
      "type": "stdio",
      "command": "daid",
      "args": ["mcp"]
    }
  }
}
```

#### 実装時の注意事項

1. **既存サービスの再利用**: `PartService`, `HashService`, `SearchService` 等を DI で注入
2. **ベクトル検索グレースフルデグラデーション**: ベクトルインデックス未構築時は `SearchDesigns` ツールを無効化し、他ツールは継続
3. **ログ出力**: STDIO トランスポート使用時、ログは stderr に出力（stdout は MCP 通信に使用）
4. **将来の HTTP 対応**: チーム共有機能実装時に Streamable HTTP トランスポートを追加検討

#### 実装開始条件

- [ ] `ModelContextProtocol` NuGet パッケージが安定版（1.0 以上）をリリース
- [ ] MCP 仕様のメジャーバージョンが安定

### 関連ドキュメント

#### テスト関連
- [docs/TEST_SCENARIO.md](./docs/TEST_SCENARIO.md) - CLI コマンドテストシナリオ

#### スクリプト
| スクリプト | 用途 |
|-----------|------|
| [scripts/test-all.ps1](./scripts/test-all.ps1) | CLI 統合テスト（PowerShell） |
| [scripts/test-all.sh](./scripts/test-all.sh) | CLI 統合テスト（bash） |
| [scripts/Run-Tests.ps1](./scripts/Run-Tests.ps1) | ユニットテスト実行 |
| [scripts/Setup-TestData.ps1](./scripts/Setup-TestData.ps1) | テストデータ作成 |

#### 将来追加予定
- [GUI_POLICY.md](./GUI_POLICY.md) - GUI設計ポリシー（GUI追加時に作成）

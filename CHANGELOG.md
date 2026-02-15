# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.2-alpha] - 2026-02-16

### Fixed

- **トリミング付きパブリッシュで EF Core のリフレクションエラーが発生するバグを修正**
  - `PublishTrimmed=true` で単一ファイルパブリッシュした場合、`EntryCurrentValueComparer<Guid>` のコンストラクタがトリミングされ `Database.Migrate()` が失敗していた
  - `TrimMode=partial` と `TrimmerRootAssembly` を csproj に追加し、EF Core アセンブリをトリミング対象外に設定

### Changed

- `DESIGN.md` の発行セクションにトリミング付きパブリッシュの注意事項を追記

## [0.3.1-alpha] - 2026-02-16

### Fixed

- **プロジェクトルート解決ロジックを刷新**
  - `DesignAid.sln` 検索（開発環境専用）→ `design_aid.db` の上方向探索（最大2階層）に変更
  - プロジェクト外からのコマンド実行時に適切なエラーメッセージを表示（exit code 3）
  - 全25+コマンドに `EnsureDataDirectory()` ガードを追加

- **`daid setup` コマンドの引数体系を修正**
  - `--path` オプション → 位置引数 `name` に変更
  - `daid setup`: カレントディレクトリをプロジェクトルートとして初期化
  - `daid setup <name>`: サブディレクトリを作成して初期化
  - 既存プロジェクトの重複初期化防止（`--force` で上書き可）

- **`daid restore` が `data/` サブディレクトリに復元するバグを修正**
  - 既存のプロジェクトルートがあればそこに復元するよう変更

- **`daid backup` の独自パス解決を `CommandHelper` に統一**

### Changed

- テストスクリプト (`test-all.ps1`) を DLL 直接実行方式に変更（`dotnet run --project` の CWD 問題を回避）
- `TEST_SCENARIO.md` を新しい setup 仕様に対応
- `DESIGN.md` のデータディレクトリ解決セクションを更新

## [0.3.0-alpha] - 2026-02-15

### Added

- **Web ダッシュボード (`daid dashboard`)**
  - Blazor Server + MudBlazor によるダッシュボード UI
  - `daid dashboard [--port 5180] [--no-browser]` で起動
  - `daid dashboard stop` で停止
  - PID ファイルによる重複起動防止、Graceful Shutdown 対応
  - 5 画面: ダッシュボード（トップ）、パーツ一覧、装置一覧、整合性チェック、類似検索

- **DI 基盤 (ServiceCollectionExtensions)**
  - 全サービスのインターフェース抽出（8 インターフェース）
  - `AddDesignAidServices()` 拡張メソッドによる一括 DI 登録
  - CLI / Dashboard / 将来の Avalonia UI で Application Layer を共有する設計

- **DashboardService**
  - ダッシュボードサマリー取得（装置数・パーツ数・ステータス集計）
  - パーツ一覧（種別・ステータス・テキストフィルター）
  - 装置一覧・詳細（パーツ展開）
  - 整合性チェック実行
  - 類似検索（ISearchService 経由）

- **DashboardServiceTests**
  - 11 件のユニットテスト追加

### Changed

- **csproj SDK を `Microsoft.NET.Sdk.Web` に変更**
  - Blazor Server の Razor コンパイルに対応
  - `IsPackable=true` を明示（Web SDK デフォルトは false）

- **サービスクラスにインターフェース実装を追加**
  - AssetService, PartService, HashService, SyncService, SearchService,
    SettingsService, ValidationService, DeployService

## [0.2.0-alpha] - 2026-02-15

### Changed

- **ベクトル検索を組み込み HNSW に移行**
  - Qdrant（Docker）依存を完全に除去
  - SQLite BLOB + HNSW ライブラリによる組み込みベクトル検索に置き換え
  - Docker / docker-compose.yml が不要に
  - `daid check` の Qdrant 接続チェックを削除

- **設定値を DB (Settings テーブル) に統合**
  - `config.json` / `appsettings.json` / 環境変数（`DA_DB_PATH` 等）を廃止
  - 全設定を SQLite の Settings テーブルに一元管理
  - ブートストラップは `DA_DATA_DIR` 環境変数（または慣例 `./data`）のみ
  - 既存の `config.json` は `daid setup` 時に自動で DB に移行
  - `daid config show/set` が DB ベースで動作

### Removed

- **Qdrant 依存の削除**
  - `Qdrant.Client` NuGet パッケージを削除
  - `QdrantService.cs` を削除
  - `docker-compose.yml` を削除
  - Qdrant 統合テストを削除

- **旧設定ファイルの廃止**
  - `appsettings.json` / `appsettings.Development.json` を削除
  - `Configuration/AppSettings.cs` を削除
  - `Configuration/DependencyInjection.cs` を削除
  - `Microsoft.Extensions.Configuration.*` / `DependencyInjection` NuGet パッケージを削除

### Added

- **VectorSearchService（組み込み HNSW）**
  - SQLite `VectorIndex` テーブルでベクトルデータを永続化
  - HNSW ライブラリによる近似最近傍探索
  - `daid sync --include-vectors` でインデックス構築
  - 外部サービス不要で `daid search` が動作

- **SettingsService**
  - Settings テーブルの CRUD 操作
  - 12 個のデフォルト設定値を管理
  - `config.json` からの自動マイグレーション機能
  - 型付きゲッター（Get / GetBool / GetInt）

## [0.1.6-alpha] - 2026-02-07

### Fixed

- **Qdrant コレクション名の環境別分離**
  - `setup` 時にデータディレクトリ名からユニークなコレクション名を自動生成
  - 複数環境で Qdrant データが混在する問題を解消
  - `search`, `sync`, `status` コマンドが config.json のコレクション名を使用するよう修正
  - `status` コマンドでコレクション名を表示

- **パッケージメタデータの修正**
  - Authors、PackageProjectUrl、RepositoryUrl のプレースホルダーを正しい値に更新

### Added

- **MCP サーバー設計検討** を DESIGN.md に追記（実装は SDK 安定版待ち）

## [0.1.3-alpha] - 2026-02-05

### Added

- **Windows インストーラー対応**
  - Inno Setup インストーラー（.exe）
  - WiX MSI インストーラー（.msi）
  - PATH 環境変数への自動追加オプション

- **日本語リリースノート**
  - CHANGELOG.md から自動抽出
  - リリースページにインストール手順を記載

- GitHub Actions CI/CD ワークフロー
- GitHub Release による自動配布
- CONTRIBUTING.md コントリビューションガイド
- CLI 統合テストスクリプト（PowerShell / bash）
- テストシナリオドキュメント（docs/TEST_SCENARIO.md）
- アーカイブ機能のテストシナリオ
- コマンド追加時のテスト必須要件（DESIGN.md）

### Changed

- README.md を公開リポジトリ用に整備
- バッジ（CI、Release、License、.NET、Platform）を追加

### Fixed

- `update` コマンドの Trimming 対応（JSON Source Generator 使用）
- バッチファイルのエンコーディングを UTF-8 に修正

## [0.1.0-alpha] - 2026-02-05

### Added

- **アーカイブ機能（容量節約）**
  - `archive asset <name>` - 装置をアーカイブ
  - `archive part <part-number>` - パーツをアーカイブ
  - `archive list` - アーカイブ一覧表示
  - `archive restore asset <name>` - 装置を復元
  - `archive restore part <part-number>` - パーツを復元

- **装置追加時の git init デフォルト化**
  - `asset add` コマンドで自動的に git init
  - `--no-git` オプションで無効化可能

- **Project 概念の削除**
  - シンプルな `assets/` + `components/` 構造に変更
  - 階層構造を簡略化

- **update コマンド**
  - `daid update` でツールを最新版に更新
  - `--version` / `-v` オプションでバージョン表示

- **グローバルツール対応**
  - `dotnet tool install --global DesignAid` でインストール可能
  - `daid` コマンドとして利用可能

- **CLI 完全実装（Phase 2）**
  - `setup` - データディレクトリ初期化
  - `config show/set/path` - 設定管理
  - `backup` - バックアップ（ZIP/S3）
  - `restore` - 復元

- **CLI 基盤実装（Phase 1）**
  - `asset add/list/remove/link/unlink` - 装置管理
  - `part add/list/remove/link` - パーツ管理
  - `check` - 整合性検証
  - `verify` - 設計基準バリデーション
  - `sync` - ファイルシステムと DB の同期
  - `deploy` - 手配パッケージ作成
  - `search` - 類似設計検索（Qdrant）
  - `status` - システム状態表示

### Technical Details

- **言語**: C# 13 / .NET 10.0
- **CLI フレームワーク**: System.CommandLine 2.0
- **ORM**: Entity Framework Core 10.0 (SQLite)
- **Vector Search**: 組み込み HNSW（SQLite BLOB + HNSW ライブラリ）

[Unreleased]: https://github.com/satorunnlg/design-aid/compare/v0.2.0-alpha...HEAD
[0.2.0-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.6-alpha...v0.2.0-alpha
[0.1.6-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.5-alpha...v0.1.6-alpha
[0.1.3-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.0-alpha...v0.1.3-alpha
[0.1.0-alpha]: https://github.com/satorunnlg/design-aid/releases/tag/v0.1.0-alpha

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.6.0-alpha] - 2026-08-15

### Changed

- **MCP の C# SDK を `ModelContextProtocol` 1.0.0 → 2.2.0 へ更新し、現行仕様 2026-07-28 に対応**（#4）
  - 2026-07-28 は後方非互換の改訂で、`initialize` ハンドシェイクに代わる**リクエスト単位の
    バージョン折衝**（`_meta` の `io.modelcontextprotocol/protocolVersion`）と、
    必須 RPC **`server/discover`** が入った。Roots / Sampling / Logging は Deprecated
  - 従来は SDK 1.0.0 に固定されており、新しいクライアントが旧ハンドシェイクへ
    落ちてくれることに依存していた
  - **本リポジトリのコード変更は不要だった**。MCP 面は stdio + 属性ベースのツール定義だけで、
    `IMcpServer` / Sampling / Roots / Tasks / OAuth / HTTP トランスポートを使っていないため、
    折衝と後方互換は SDK が担う。ビルド警告（MCP9005 等）も出ていない
  - 実測で確認: `server/discover` が `supportedVersions: ["2026-07-28"]` を返し、
    新仕様で 13 ツールの列挙と実行が通る。**旧ハンドシェイク（2025-06-18）でも 13 ツール列挙**

### Fixed

- **MCP サーバーが名乗るバージョンがハードコードされていた**
  - `McpCommand.cs` の `ServerInfo.Version` が文字列直書きで、**リリースのたびに取り残されていた**
  - 同じ取得コードが `UpdateCommand` と `DesignAidMcpTools` にも複製されていた
  - `AppInfo.Version` に集約し、csproj の `<Version>` を唯一の正本にした

## [0.5.5-alpha] - 2026-08-15

### Fixed

- **`asset add` が同じ装置に ID を 2 つ採番していた問題を修正**（#2）
  - `asset.json` 用に `Guid.NewGuid()`、DB 用に `Asset.Create()` が別の Guid を採番しており、
    同じ装置の ID が参照先によって変わっていた。コンソールに出るのは `asset.json` 側だけなので気付けなかった
  - `Asset.Create(..., id:)` を追加し、1 つの Guid を両方へ書くようにした

### Added

- **`asset.json` と DB の乖離を検出・復旧できるようにした**（#1）
  - `asset list` / `asset bom` は `asset.json` しか見ないため、**DB にしか無い装置は
    存在しないように見える**。エラーも出ず、通常の `sync` でも直らなかった
    （`sync` は `asset.json → DB` の一方向 UPSERT で、`asset.json` が無いディレクトリを素通りする）
  - `daid sync` が常にこの欠落を検査し、`[JSON MISSING]` として報告するようにした
  - `daid sync --restore-json` で DB の内容から `asset.json` を復元できるようにした
    - **DB は変更しない**。`asset.json` に書く ID は DB の行の ID をそのまま使う
    - **既存の `asset.json` は上書きしない**（ファイル側が正本）
    - ディレクトリが `assets/` に無い装置は対象外（アーカイブ済みを誤報しないため）
    - `created_at` は UTC で書き、`asset add` と表記を揃える
  - `daid asset add <name> --no-git` も、DB に既存行があればその ID を引き継いで
    `asset.json` だけを作る復旧経路として働く（出力は `Asset json restored:`）
  - `sync --json` の出力に `assetJsonGaps` を追加

## [0.5.4-alpha] - 2026-05-28

### Fixed

- **write 系コマンド（`asset add`/`update` 等）のハングを修正**
  - `CommandHelper.CreateDbContext()` が毎回 `Database.Migrate()` を呼んでおり、過去の Migrate 中の異常終了で `__EFMigrationsLock` に残留したロック行があると、EF Core の `SqliteHistoryRepository` がロック取得を無限リトライし、write 系コマンドがハングしていた（読み取り系は `Migrate()` を通らないため影響なし）
  - 未適用のマイグレーションがある場合のみ `Migrate()` を実行するよう変更し、通常運用（pending なし）ではマイグレーションロックを取得しないようにした

## [0.4.6-alpha] - 2026-02-24

### Fixed

- **ダッシュボード類似検索のしきい値を修正**
  - 検索しきい値が 0.5 とハードコードされており、実際の類似度スコア（0.2〜0.4）を超えるため結果が常に 0 件だった
  - CLI のデフォルト（0.3）に合わせて修正

## [0.4.5-alpha] - 2026-02-24

### Fixed

- **トリミング付きパブリッシュでダッシュボードが動作しないバグを修正**
  - `PublishTrimmed=true` により `AntiforgeryRequestToken` のプロパティメタデータが削除され、Blazor 回路の初期化が失敗していた
  - Blazor Server / MudBlazor / JSInterop / System.CommandLine の各アセンブリを `TrimmerRootAssembly` に追加しトリミングから保護
  - リリースバイナリ（GitHub Actions ビルド）でダッシュボードのインタラクティブ機能が正常に動作するようになった

## [0.4.4-alpha] - 2026-02-24

### Fixed

- **ダッシュボードの Blazor 回路エラーを修正**
  - `AddDbContextFactory` と `AddDbContext` の二重登録による DI スコープ解決エラーを修正
  - メニュー開閉・フィルター・ダイアログ等のインタラクティブ機能が動作するようになった
  - 原因: `DbContextOptions` の競合により Blazor SignalR 回路が起動直後に停止していた

## [0.4.3-alpha] - 2026-02-24

### Added

- **CLIコマンドの DB dual-write 対応**
  - `asset add/update/remove/link/unlink` が JSON と SQLite DB の両方に書き込むように変更
  - `part add/update/remove/link` が JSON と SQLite DB の両方に書き込むように変更
  - `daid dashboard` が正しくデータを表示できるようになった

- **`daid sync` の DB 同期機能**
  - `daid sync` 実行時に assets/ と components/ の全データを DB に UPSERT
  - 既存の47アセット + パーツを一括で DB に投入可能
  - `--skip-db` オプションで DB 同期をスキップ可能

- **`CommandHelper.CreateDbContext()` ヘルパーメソッド**
  - DbContext 生成とマイグレーション自動適用を共通化
  - SyncCommand のベクトルインデックス同期も統一

### Changed

- `SyncCommand` のベクトルインデックス同期で直接 DbContext を生成していた箇所を `CommandHelper.CreateDbContext()` に統一

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

[Unreleased]: https://github.com/satorunnlg/design-aid/compare/v0.4.6-alpha...HEAD
[0.4.6-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.4.5-alpha...v0.4.6-alpha
[0.4.5-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.4.4-alpha...v0.4.5-alpha
[0.4.4-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.4.3-alpha...v0.4.4-alpha
[0.4.3-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.4.2-alpha...v0.4.3-alpha
[0.2.0-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.6-alpha...v0.2.0-alpha
[0.1.6-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.5-alpha...v0.1.6-alpha
[0.1.3-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.0-alpha...v0.1.3-alpha
[0.1.0-alpha]: https://github.com/satorunnlg/design-aid/releases/tag/v0.1.0-alpha

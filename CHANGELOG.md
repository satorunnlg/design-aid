# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **終了コードの変更（#8）:** `Environment.ExitCode` を設定するだけのハンドラが、失敗しても終了コード 0 を返していたのを直した。
  `Program.Main` が `InvokeAsync` の戻り値が 0 のとき `Environment.ExitCode` を返す。**これまで 0 だった失敗が 0 以外になる**
  （プロジェクトが無い場所の `asset list` などは 3、`check` の整合性エラーは 5 など）。終了コードが 0 であることを前提にしたスクリプトは直す
- `--json` のコマンドは、初期化の失敗や例外でも標準出力が有効な JSON 1 つになる（`{ "success": false, "errors": [...], "exitCode": N }`、
  診断は標準エラー）。以前は標準出力が空になることがあった（#10）
- `asset rename` が、壊れた zip（検査できないアーカイブ）で止まるとき、エラーに zip のパスと直し方（その zip を退避する、または
  `daid archive restore asset <名前>` で戻す）を出す。止める判定は変えていない（#10）

### Fixed

- `asset rename` が部品の索引行の装置名を書き換えるとき `UpdatedAt` を進めていなかったため、検索のキャッシュの古さの判定が検知できなかった。
  VectorIndex の行を書き換える箇所がすべて `UpdatedAt` を進めることを、静的な検査で固定した（#10）
- プロジェクトのロックの再入の判定で、junction・symlink 経由の同じ場所を別の場所と見なして自分自身のロックに止められていたのを、
  リンクを解決した実体のパスで判定するように直した（#10）
- 検索のキャッシュを新しい形式で書く前に旧形式の `.stamp` を消し、消せなければ書かない（旧版の daid が壊れた bin を読む隙をなくした）（#10）
- `CommandHelper.BuildServiceProvider` も、ファクトリを登録する前に保留中のマイグレーションの経路を通すようにした（ダッシュボード・MCP と同じ）（#10）

### Added

- `BackupCommand.Uploader`（S3 へのアップロードの差し替え口）。backup がアップロードの前にロックを解放することの検査のため（#10）

## [0.6.2-alpha] - 2026-11-15

### Added

- **`daid asset rename <old-name> <new-name>`**（#7）。ID を変えずに装置の名前とフォルダを変える
  - asset.json と DB の両方で同じ ID のまま `name` だけを変え、`assets/<old>/` を `assets/<new>/` へ
    git ごと移す。git のリモートは変えない（終わりに表示する）
  - asset.json に `previous_names`（古い順・重複なし）を追加し、変更前の名前を残す。DB の `Assets` にも
    `PreviousNames` 列を追加（マイグレーション `AddAssetPreviousNames`。次に daid を実行したとき自動で適用される）。
    `asset update` / `sync` / `sync --restore-json` はこの欄を落とさずに往復させる
  - 名前や ID で結ばれたものを同じ処理で揃える: 他の装置の `asset_links.json` の子装置の参照、
    DB の `DirectoryPath`、ベクトル索引の装置名と文書のパス。部品・子装置のリンクは ID で結ばれているので引き継がれる
  - 事前確認（旧名なし・新名が不正・新名が既にある・移動先が既にある・旧名と新名が同じ）に 1 つでも当てはまれば
    何も変えずに終了コード 1 で止まる。途中で失敗したら逆順に戻す
  - `--json` / `--quiet`
  - 注意: 0.6.1-alpha 以前の daid の `asset update` は `previous_names` を消す。改名を使ったら、同じ DB と assets/ を扱う daid を全部更新する
- プロジェクト共通のロック（`.daid.lock`。置いたままにして `FileShare.None` で保持し、待たずに止める）。sync・setup・
  asset add/update/remove/rename/link/unlink・part add/update/remove/link・archive asset/part/restore・deploy・restore・backup・
  ダッシュボードの更新・MCP の書き込み系が参加し、同時の実行で中間状態を取り込み合わないようにした。
  参加の漏れは静的な検査（`ProjectLockCoverageTests`）が捕まえる
- 改名のあと、文書チャンクの索引行を消し、HNSW のキャッシュを無効にする（起動中の検索も VectorIndex の変化を件数・Id・更新時刻の
  判定値で検知して作り直す。ディスクのキャッシュにも構築時の判定値を付け、一致しなければ使わない）
- 装置名: `CONIN$`・`CONOUT$` と上付き数字の予約名（`COM¹` など）と、末尾の空白付きの予約名（`CON .txt`）を拒否。新しく付ける名前は UTF-8 で 255 バイトまで
  （macOS の NFD 分解後の長さも数える）
- `asset add` が装置名を検査するようになった（`asset rename` と同じ検査。空・パス区切り・`..`・
  Windows の禁止文字や予約名・末尾の `.` を終了コード 2 で拒否する）

### Fixed

- `--json` を付けて成功したときも、標準出力は JSON 1 つだけになる（#10）。`sync --json` で `components/` が無いとき、JSON のあとに
  `Syncing to database...` と `[SUCCESS] ...` を標準出力へ出していた。`--json` のときの人向けの出力は標準エラーへ向ける
  （`CommandHelper.HumanOutput`）。同じ種類として、`asset bom --json --export csv` が JSON でなく CSV を標準出力へ出していたのも直した
  （`--json` を優先し、CSV は出さずに警告を標準エラーへ）。`--json` を持つ全コマンドを成功の経路で動かす検査
  （`JsonStdoutContractTests`。コマンドは `BuildRootCommand()` から列挙する）を足した
- `--json` を付けたまま引数の解析に失敗したとき（例: `asset list --json --bogus`）、ヘルプの文が標準出力に出ていた。
  標準出力は失敗の JSON 1 つだけ、診断は標準エラー、終了コードは 1 になる（#10）
- プロジェクトのロックの再入の判定が macOS でも大文字小文字を区別しており（`Ordinal`）、既定の大小文字を区別しないボリュームで、
  大小文字だけ違うパスを再入と見なさず自分のロックで止まっていた。Windows と同じく macOS も区別しない比較にした（#10）

## [0.6.1-alpha] - 2026-10-12

### Fixed

- MCP `GetProjectContext` が AGENTS.md に対応。指示ファイルは AGENTS.md を優先し、無ければ CLAUDE.md を読む
  （移行済みアセットで言語ルールが空になっていた）。読んだファイルを返り値 `instructionsFile` に追加。
  `hasClaude` は互換のため残し、意味を「指示ファイルがある」とした

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

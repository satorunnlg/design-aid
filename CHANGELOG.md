# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- **Vector DB**: Qdrant 1.x（オプション）

[Unreleased]: https://github.com/satorunnlg/design-aid/compare/v0.1.3-alpha...HEAD
[0.1.3-alpha]: https://github.com/satorunnlg/design-aid/compare/v0.1.0-alpha...v0.1.3-alpha
[0.1.0-alpha]: https://github.com/satorunnlg/design-aid/releases/tag/v0.1.0-alpha

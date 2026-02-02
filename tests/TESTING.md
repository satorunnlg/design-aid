# テスト戦略

## 現在のテスト状況

| カテゴリ | テスト数 | 状態 |
|---------|---------|------|
| ユニットテスト | 60 | ✅ 全成功 |
| 統合テスト | 4 | ✅ 全成功 |
| **合計** | **64** | **✅ 全成功** |

## 概要

Design Aid のテストは以下の3層で構成する。

| レベル | 対象 | 実行方法 | 頻度 |
|--------|------|---------|------|
| ユニットテスト | ドメインモデル、サービス | `dotnet test` | コミット毎 |
| 統合テスト | サービス連携、ファイルI/O | `dotnet test --filter Integration` | PR毎 |
| E2Eテスト | CLIコマンド全体 | `dotnet test --filter E2E` | リリース前 |

## クイックスタート

```powershell
# 全テスト実行
dotnet test

# ユニットテストのみ
dotnet test --filter "Category!=Integration&Category!=E2E"

# 統合テストのみ
dotnet test --filter "Category=Integration"

# 特定テストクラス
dotnet test --filter "FullyQualifiedName~HashServiceTests"

# 特定テストメソッド
dotnet test --filter "Name=ComputeHash_ValidFile_ReturnsCorrectHash"
```

## テストデータ構造

開発用テストデータは `data/` に配置する。コンポーネントは共有リソースとして `data/components/` に配置し、Asset との紐付けは DB の中間テーブルで管理する。

```
data/
├── config.json                    # 開発用設定（将来）
├── design_aid.db                  # 開発用DB（gitignore）
├── components/                    # 共有コンポーネント
│   └── SP-TEST-001/
│       ├── part.json
│       └── drawing.txt
└── projects/
    └── sample-project/            # サンプルプロジェクト
        ├── .da-project            # プロジェクトマーカー
        └── assets/
            └── lifting-unit/      # サンプル装置
                └── asset.json
```

## 統合テストの実行

統合テストは実際のファイルシステムを使用してサービス連携を検証する。

```powershell
# 統合テスト実行
dotnet test --filter "Category=Integration" --logger "console;verbosity=detailed"
```

## テストカテゴリ

テストには以下のカテゴリを付与する：

```csharp
[Trait("Category", "Unit")]        // ユニットテスト（デフォルト）
[Trait("Category", "Integration")] // 統合テスト
[Trait("Category", "E2E")]         // E2Eテスト
```

## デバッグ方法

### Visual Studio / VS Code

1. テストエクスプローラーで対象テストを右クリック
2. 「デバッグ」を選択
3. ブレークポイントで停止

### コマンドライン

```powershell
# 詳細ログ付きで実行
dotnet test --logger "console;verbosity=detailed"

# 失敗時に停止
dotnet test --blame

# 特定テストをデバッグ実行
dotnet test --filter "Name=ComputeHash_ValidFile_ReturnsCorrectHash" -- RunConfiguration.BreakOnFailure=true
```

## サンプルプロジェクト作成スクリプト

PowerShellで実行：

```powershell
# data/projects/sample-project を作成
.\scripts\Setup-TestData.ps1
```

## 継続的テスト

開発中は `dotnet watch test` で変更を検知して自動実行：

```powershell
dotnet watch test --project tests/DesignAid.Tests
```

# Design Aid CLI テストシナリオ

このドキュメントは、Design Aid CLI の全コマンドをテストするためのシナリオを定義します。
空の data ディレクトリから開始し、全ての操作を実行後、再び空の data ディレクトリに戻ります。

## 前提条件

- .NET 10.0 SDK がインストールされていること
- プロジェクトがビルドされていること
- `data/` ディレクトリが存在しないか、空であること

## テスト実行環境

```bash
# プロジェクトルートで実行
cd c:/Users/kisar/github/design-aid

# ビルド確認
dotnet build

# data ディレクトリが存在する場合は削除
rm -rf data/
```

---

## Phase 1: 初期化 (Setup & Config)

### 1.1 ヘルプの確認

```bash
# ルートヘルプ
dotnet run --project src/DesignAid -- --help

# 期待結果: コマンド一覧が表示される
```

### 1.2 データディレクトリの初期化

```bash
# setup コマンドを実行
dotnet run --project src/DesignAid -- setup

# 期待結果:
# - data/ ディレクトリが作成される
# - data/components/ ディレクトリが作成される
# - data/projects/ ディレクトリが作成される
# - data/config.json が作成される
# - data/.gitignore が作成される
```

### 1.3 設定の確認

```bash
# 設定を表示
dotnet run --project src/DesignAid -- config show

# パス情報を表示
dotnet run --project src/DesignAid -- config path

# 期待結果: 設定内容が表示される
```

### 1.4 設定の変更

```bash
# Qdrant を無効化（ローカルテスト用）
dotnet run --project src/DesignAid -- config set qdrant.enabled false

# 設定を確認
dotnet run --project src/DesignAid -- config show

# 期待結果: qdrant.enabled が false になっている
```

---

## Phase 2: プロジェクト管理

### 2.1 プロジェクト一覧（空）

```bash
dotnet run --project src/DesignAid -- project list

# 期待結果: "登録されているプロジェクトはありません" または空の一覧
```

### 2.2 プロジェクトの追加

```bash
# テスト用プロジェクトディレクトリを作成
mkdir -p data/projects/test-project

# プロジェクトを登録
dotnet run --project src/DesignAid -- project add data/projects/test-project

# 期待結果: プロジェクトが登録される
```

### 2.3 プロジェクト一覧（1件）

```bash
dotnet run --project src/DesignAid -- project list

# 期待結果: test-project が表示される
```

### 2.4 複数プロジェクトの追加

```bash
# 2つ目のプロジェクト
mkdir -p data/projects/sample-machine

dotnet run --project src/DesignAid -- project add data/projects/sample-machine --name "サンプル機械"

# 期待結果: 2つ目のプロジェクトが登録される
```

### 2.5 プロジェクト一覧（複数件）

```bash
dotnet run --project src/DesignAid -- project list

# 期待結果: 2つのプロジェクトが表示される
```

---

## Phase 3: 装置（Asset）管理

### 3.1 装置一覧（空）

```bash
dotnet run --project src/DesignAid -- asset list --project test-project

# 期待結果: 装置がない旨のメッセージ
```

### 3.2 装置の追加

```bash
# 装置を追加
dotnet run --project src/DesignAid -- asset add lifting-unit --project test-project --display-name "昇降ユニット"

# 期待結果: 装置が追加される
```

### 3.3 複数装置の追加

```bash
dotnet run --project src/DesignAid -- asset add control-panel --project test-project --display-name "制御盤"

dotnet run --project src/DesignAid -- asset add conveyor-a --project sample-machine --display-name "コンベアA"

# 期待結果: 装置が追加される
```

### 3.4 装置一覧

```bash
dotnet run --project src/DesignAid -- asset list --project test-project

# 期待結果: lifting-unit と control-panel が表示される
```

---

## Phase 4: パーツ（Part/Component）管理

### 4.1 パーツ一覧（空）

```bash
dotnet run --project src/DesignAid -- part list

# 期待結果: パーツがない旨のメッセージ
```

### 4.2 パーツの追加

```bash
# 製作物パーツを追加
dotnet run --project src/DesignAid -- part add BASE-PLATE-001 --type Fabricated --name "ベースプレート"

# 期待結果: パーツが追加される
```

### 4.3 異なるタイプのパーツ追加

```bash
# 購入品
dotnet run --project src/DesignAid -- part add MTR-001 --type Purchased --name "サーボモーター"

# 規格品
dotnet run --project src/DesignAid -- part add BOLT-M10-30 --type Standard --name "六角ボルト M10x30"

# 期待結果: 各タイプのパーツが追加される
```

### 4.4 パーツ一覧

```bash
dotnet run --project src/DesignAid -- part list

# 期待結果: 3つのパーツが表示される
```

### 4.5 パーツと装置のリンク

```bash
# パーツを装置にリンク
dotnet run --project src/DesignAid -- part link BASE-PLATE-001 --asset lifting-unit --quantity 1

dotnet run --project src/DesignAid -- part link MTR-001 --asset lifting-unit --quantity 2

dotnet run --project src/DesignAid -- part link BOLT-M10-30 --asset lifting-unit --quantity 8

# 期待結果: パーツが装置にリンクされる
```

---

## Phase 5: コア機能

### 5.1 ステータス確認

```bash
dotnet run --project src/DesignAid -- status

# 期待結果: システム全体のステータスが表示される
```

### 5.2 整合性チェック

```bash
dotnet run --project src/DesignAid -- check

# 期待結果: チェック結果が表示される（成果物がないので警告かもしれない）
```

### 5.3 同期

```bash
dotnet run --project src/DesignAid -- sync

# 期待結果: 同期結果が表示される
```

### 5.4 設計基準検証

```bash
dotnet run --project src/DesignAid -- verify

# 期待結果: 検証結果が表示される
```

---

## Phase 6: 検索

### 6.1 キーワード検索

```bash
# Qdrant が無効なので、この機能は制限されるかもしれない
dotnet run --project src/DesignAid -- search "ベースプレート"

# 期待結果: 検索結果または Qdrant 無効のメッセージ
```

---

## Phase 7: 手配（Deploy）

### 7.1 手配パッケージ確認

```bash
dotnet run --project src/DesignAid -- deploy --dry-run

# 期待結果: 手配対象の確認（ドライラン）
```

---

## Phase 8: バックアップ

### 8.1 ローカルバックアップ

```bash
# ローカルに ZIP を作成
dotnet run --project src/DesignAid -- backup --local-only

# 期待結果: design-aid-backup_YYYYMMDD_HHMMSS.zip が作成される
```

### 8.2 バックアップファイルの確認

```bash
dir *.zip

# 期待結果: バックアップ ZIP ファイルが存在する
```

### 8.3 バックアップファイルの削除

```bash
del *.zip

# 期待結果: ZIP ファイルが削除される
```

---

## Phase 9: クリーンアップ（削除操作）

### 9.1 パーツの削除

```bash
dotnet run --project src/DesignAid -- part remove BOLT-M10-30

dotnet run --project src/DesignAid -- part remove MTR-001

dotnet run --project src/DesignAid -- part remove BASE-PLATE-001

# 期待結果: パーツが削除される
```

### 9.2 パーツ一覧（空確認）

```bash
dotnet run --project src/DesignAid -- part list

# 期待結果: パーツがない
```

### 9.3 装置の削除

```bash
dotnet run --project src/DesignAid -- asset remove lifting-unit --project test-project

dotnet run --project src/DesignAid -- asset remove control-panel --project test-project

dotnet run --project src/DesignAid -- asset remove conveyor-a --project sample-machine

# 期待結果: 装置が削除される
```

### 9.4 装置一覧（空確認）

```bash
dotnet run --project src/DesignAid -- asset list --project test-project

# 期待結果: 装置がない
```

### 9.5 プロジェクトの削除

```bash
dotnet run --project src/DesignAid -- project remove test-project

dotnet run --project src/DesignAid -- project remove sample-machine

# 期待結果: プロジェクトが削除される
```

### 9.6 プロジェクト一覧（空確認）

```bash
dotnet run --project src/DesignAid -- project list

# 期待結果: プロジェクトがない
```

---

## Phase 10: 完全クリーンアップ

### 10.1 data ディレクトリの削除

```bash
# data ディレクトリを完全に削除
rm -rf data/

# ディレクトリが存在しないことを確認
dir data/

# 期待結果: "ファイルが見つかりません" または同等のエラー
```

---

## テスト完了チェックリスト

- [ ] Phase 1: setup, config コマンドが正常動作
- [ ] Phase 2: project add/list/remove が正常動作
- [ ] Phase 3: asset add/list/remove が正常動作
- [ ] Phase 4: part add/list/link/remove が正常動作
- [ ] Phase 5: status, check, sync, verify が正常動作
- [ ] Phase 6: search が正常動作（または適切なエラー）
- [ ] Phase 7: deploy が正常動作
- [ ] Phase 8: backup が正常動作
- [ ] Phase 9: 全削除操作が正常動作
- [ ] Phase 10: data/ ディレクトリが完全に削除可能

## 備考

- Qdrant を使用する機能（search）は、Qdrant が起動していない場合はスキップまたはエラーになります
- AWS S3 バックアップは AWS CLI がインストールされていない場合はスキップしてください
- 各コマンドのヘルプは `--help` オプションで確認できます

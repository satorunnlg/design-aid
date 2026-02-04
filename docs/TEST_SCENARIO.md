# Design Aid CLI テストシナリオ

このドキュメントは、Design Aid CLI (`daid`) の全コマンドをテストするためのシナリオを定義します。
空の data ディレクトリから開始し、全ての操作を実行後、再び空の data ディレクトリに戻ります。

## 前提条件

- .NET 10.0 SDK がインストールされていること
- `daid` がグローバルツールとしてインストールされていること
- `data/` ディレクトリが存在しないか、空であること

## グローバルツールのインストール

```bash
# プロジェクトルートで実行
cd c:/Users/kisar/github/design-aid

# ビルド
dotnet build

# グローバルツールとしてインストール（既存を上書き）
dotnet tool install --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*"

# または既存の更新
dotnet tool update --global --add-source ./src/DesignAid/bin/Debug/net10.0/ DesignAid --version "*-*"

# data ディレクトリが存在する場合は削除
rm -rf data/
```

---

## Phase 1: 初期化 (Setup & Config)

### 1.1 ヘルプの確認

```bash
# ルートヘルプ
daid --help

# 期待結果: コマンド一覧が表示される
# - setup, config, asset, part, check, verify, sync, status, deploy, search, backup, restore, update
```

### 1.2 データディレクトリの初期化

```bash
# setup コマンドを実行
daid setup

# 期待結果:
# - data/ ディレクトリが作成される
# - data/assets/ ディレクトリが作成される
# - data/components/ ディレクトリが作成される
# - data/config.json が作成される
# - data/.gitignore が作成される
```

### 1.3 設定の確認

```bash
# 設定を表示
daid config show

# パス情報を表示
daid config path

# 期待結果: 設定内容が表示される
```

### 1.4 設定の変更

```bash
# Qdrant を無効化（ローカルテスト用）
daid config set qdrant.enabled false

# 設定を確認
daid config show

# 期待結果: qdrant.enabled が false になっている
```

---

## Phase 2: 装置（Asset）管理

### 2.1 装置一覧（空）

```bash
daid asset list

# 期待結果: "装置はありません" または空の一覧
```

### 2.2 装置の追加

```bash
# 装置を追加
daid asset add lifting-unit --display-name "昇降ユニット"

# 期待結果:
# - 装置が追加される
# - data/assets/lifting-unit/ ディレクトリが作成される
# - data/assets/lifting-unit/asset.json が作成される
```

### 2.3 複数装置の追加

```bash
daid asset add control-panel --display-name "制御盤"

daid asset add safety-module --display-name "安全モジュール"

# 期待結果: 各装置が追加される
```

### 2.4 装置一覧（複数件）

```bash
daid asset list

# 期待結果: 3つの装置が表示される
# - lifting-unit (昇降ユニット)
# - control-panel (制御盤)
# - safety-module (安全モジュール)
```

### 2.5 子装置のリンク（SubAsset）

```bash
# safety-module を lifting-unit の子装置として組み込み
daid asset link lifting-unit --child safety-module --quantity 1 --notes "安全装置"

# 期待結果:
# - safety-module が lifting-unit の子装置としてリンクされる
# - data/assets/lifting-unit/asset_links.json にリンク情報が保存される
```

---

## Phase 3: パーツ（Part/Component）管理

### 3.1 パーツ一覧（空）

```bash
daid part list

# 期待結果: "パーツはありません" または空の一覧
```

### 3.2 製作物パーツの追加

```bash
# 製作物パーツを追加
daid part add BASE-PLATE-001 --type Fabricated --name "ベースプレート"

# 期待結果:
# - パーツが追加される
# - data/components/BASE-PLATE-001/ ディレクトリが作成される
# - data/components/BASE-PLATE-001/part.json が作成される
```

### 3.3 購入品パーツの追加

```bash
# 購入品
daid part add MTR-001 --type Purchased --name "サーボモーター"

# 期待結果: 購入品パーツが追加される
```

### 3.4 規格品パーツの追加

```bash
# 規格品
daid part add BOLT-M10-30 --type Standard --name "六角ボルト M10x30"

# 期待結果: 規格品パーツが追加される
```

### 3.5 パーツ一覧

```bash
daid part list

# 期待結果: 3つのパーツが表示される
# - BASE-PLATE-001 (Fabricated)
# - MTR-001 (Purchased)
# - BOLT-M10-30 (Standard)
```

### 3.6 パーツと装置のリンク

```bash
# パーツを装置にリンク
daid part link BASE-PLATE-001 --asset lifting-unit --quantity 1

daid part link MTR-001 --asset lifting-unit --quantity 2

daid part link BOLT-M10-30 --asset lifting-unit --quantity 8

# 期待結果:
# - パーツが装置にリンクされる
# - data/assets/lifting-unit/asset_links.json にリンク情報が保存される
```

### 3.7 リンク結果の確認

```bash
# 装置の詳細を確認（パーツ一覧、子装置含む）
daid asset list --verbose

# 期待結果:
# - lifting-unit に3つのパーツがリンクされていることが表示される
# - lifting-unit に子装置 safety-module がリンクされていることが表示される
```

---

## Phase 4: コア機能

### 4.1 ステータス確認

```bash
daid status

# 期待結果: システム全体のステータスが表示される
# - 装置数、パーツ数
# - DB 接続状態
# - Qdrant 接続状態（無効の場合は Disabled）
```

### 4.2 整合性チェック

```bash
daid check

# 期待結果: チェック結果が表示される
# - 成果物がないパーツは警告または OK
```

### 4.3 同期

```bash
daid sync

# 期待結果: 同期結果が表示される
```

### 4.4 設計基準検証

```bash
daid verify

# 期待結果: 検証結果が表示される
```

---

## Phase 5: 検索

### 5.1 キーワード検索

```bash
# Qdrant が無効なので、この機能は制限される
daid search "ベースプレート"

# 期待結果: Qdrant 無効のメッセージまたは検索結果
```

---

## Phase 6: 手配（Deploy）

### 6.1 手配パッケージ確認（ドライラン）

```bash
daid deploy --dry-run

# 期待結果: 手配対象の確認（ドライラン）
# - 成果物がある場合はデプロイ対象一覧が表示される
# - 成果物がない場合は "デプロイ対象のパーツがありません" と表示される
```

### 6.2 成果物を追加してドライラン確認

```bash
# パーツに成果物を追加
echo "テスト図面" > data/components/BASE-PLATE-001/drawing.dxf

# ドライランを再実行
daid deploy --dry-run

# 期待結果:
# - BASE-PLATE-001 がデプロイ対象として表示される
# - "[DRY-RUN] 実際のデプロイは行われません" と表示される
```

---

## Phase 7: バックアップと復元

### 7.1 ローカルバックアップ

```bash
# ローカルに ZIP を作成
daid backup --local-only

# 期待結果: design-aid-backup_YYYYMMDD_HHMMSS.zip が作成される
```

### 7.2 バックアップファイルの確認

```bash
ls -la *.zip

# 期待結果: バックアップ ZIP ファイルが存在する
```

### 7.3 バックアップファイル名を記録

```bash
# ここで作成された ZIP ファイル名をメモする
# 例: design-aid-backup_20260205_120000.zip
```

---

## Phase 8: クリーンアップ（削除操作）

### 8.1 子装置リンクの解除

```bash
# 子装置のリンクを解除
daid asset unlink lifting-unit --child safety-module

# 期待結果: safety-module と lifting-unit のリンクが解除される
```

### 8.2 パーツの削除

```bash
# 確認プロンプトに y で回答
daid part remove BOLT-M10-30

daid part remove MTR-001

daid part remove BASE-PLATE-001

# 期待結果: パーツが削除される
```

### 8.3 パーツ一覧（空確認）

```bash
daid part list

# 期待結果: パーツがない
```

### 8.4 装置の削除

```bash
# 確認プロンプトに y で回答
daid asset remove lifting-unit

daid asset remove control-panel

daid asset remove safety-module

# 期待結果: 装置が削除される
```

### 8.5 装置一覧（空確認）

```bash
daid asset list

# 期待結果: 装置がない
```

---

## Phase 9: 復元テスト

### 9.1 バックアップから復元

```bash
# Phase 7.3 でメモした ZIP ファイル名を使用
# 確認プロンプトに y で回答
daid restore ./design-aid-backup_YYYYMMDD_HHMMSS.zip

# 期待結果: データが復元される
```

### 9.2 復元後のデータ確認

```bash
daid asset list

daid part list

# 期待結果:
# - 3つの装置が復元されている
# - 3つのパーツが復元されている
```

### 9.3 再クリーンアップ

```bash
# 復元されたデータを再度削除（確認プロンプトに y で回答）
daid part remove BOLT-M10-30
daid part remove MTR-001
daid part remove BASE-PLATE-001

daid asset remove lifting-unit
daid asset remove control-panel
daid asset remove safety-module

# 期待結果: 全てのデータが削除される
```

---

## Phase 10: 完全クリーンアップ

### 10.1 バックアップファイルの削除

```bash
rm -f *.zip

# 期待結果: ZIP ファイルが削除される
```

### 10.2 data ディレクトリの削除

```bash
# data ディレクトリを完全に削除
rm -rf data/

# ディレクトリが存在しないことを確認
ls -la data/ 2>&1 || echo "data directory removed successfully"

# 期待結果: "No such file or directory" または "data directory removed successfully"
```

---

## テスト完了チェックリスト

- [ ] Phase 1: setup, config コマンドが正常動作
- [ ] Phase 2: asset add/list/link/unlink が正常動作
- [ ] Phase 3: part add/list/link/remove が正常動作
- [ ] Phase 4: status, check, sync, verify が正常動作
- [ ] Phase 5: search が正常動作（または適切なエラー）
- [ ] Phase 6: deploy --dry-run が正常動作
- [ ] Phase 7: backup が正常動作
- [ ] Phase 8: 全削除操作が正常動作
- [ ] Phase 9: restore が正常動作
- [ ] Phase 10: data/ ディレクトリが完全に削除可能

## 備考

- Qdrant を使用する機能（search）は、Qdrant が起動していない場合はスキップまたはエラーになります
- AWS S3 バックアップは AWS CLI/プロファイルが設定されていない場合はスキップしてください
- 各コマンドのヘルプは `--help` オプションで確認できます
- 削除コマンド（asset remove, part remove）は確認プロンプトが表示されます

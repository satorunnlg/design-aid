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
# - data/design_aid.db が作成される（マイグレーション適用）
# - Settings テーブルにデフォルト設定が書き込まれる
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
# ベクトル検索の有効/無効を確認
daid config show

# 設定を変更する場合
daid config set vector_search.enabled true

# 期待結果: vector_search.enabled が true になっている
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
# - ベクトルインデックス状態
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

### 5.1 ベクトルインデックスの構築

```bash
# ベクトルインデックスを構築
daid sync --include-vectors

# 期待結果: ベクトルインデックスが構築される（同期件数が表示される）
```

### 5.2 類似設計検索

```bash
# ベクトル検索（インデックス構築後）
daid search "ベースプレート"

# 期待結果: 類似パーツの検索結果が表示される
# - ベクトルインデックスが空の場合はローカルキーワード検索にフォールバック
```

### 5.3 ローカルキーワード検索

```bash
# --local オプションでキーワード検索
daid search "モーター" --local

# 期待結果: ローカルキーワード検索の結果が表示される
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

## Phase 7: アーカイブ（容量節約）

### 7.1 アーカイブ一覧（空）

```bash
daid archive list

# 期待結果: "No archived items." と表示される
```

### 7.2 装置をアーカイブ

```bash
# control-panel をアーカイブ
daid archive asset control-panel

# 期待結果:
# - data/archive/assets/control-panel.zip が作成される
# - data/archive_index.json にエントリが追加される
# - data/assets/control-panel/ が削除される
# - 圧縮率が表示される
```

### 7.3 パーツをアーカイブ

```bash
# BOLT-M10-30 をアーカイブ
daid archive part BOLT-M10-30

# 期待結果:
# - data/archive/components/BOLT-M10-30.zip が作成される
# - data/archive_index.json にエントリが追加される
# - data/components/BOLT-M10-30/ が削除される
```

### 7.4 アーカイブ一覧（確認）

```bash
daid archive list

# 期待結果:
# - Assets: control-panel が表示される
# - Parts: BOLT-M10-30 が表示される
# - サイズと節約量が表示される
```

### 7.5 アーカイブ一覧（JSON形式）

```bash
daid archive list --json

# 期待結果: JSON形式でアーカイブ情報が出力される
```

### 7.6 装置一覧・パーツ一覧（アーカイブ後）

```bash
daid asset list
daid part list

# 期待結果:
# - control-panel が装置一覧に表示されない
# - BOLT-M10-30 がパーツ一覧に表示されない
```

### 7.7 アーカイブからパーツを復元

```bash
daid archive restore part BOLT-M10-30

# 期待結果:
# - data/components/BOLT-M10-30/ が復元される
# - アーカイブファイルが削除される
# - archive_index.json からエントリが削除される
```

### 7.8 アーカイブから装置を復元

```bash
daid archive restore asset control-panel

# 期待結果:
# - data/assets/control-panel/ が復元される
# - アーカイブファイルが削除される
# - archive_index.json からエントリが削除される
```

### 7.9 復元後の確認

```bash
daid asset list
daid part list
daid archive list

# 期待結果:
# - control-panel が装置一覧に表示される
# - BOLT-M10-30 がパーツ一覧に表示される
# - アーカイブ一覧が空になる
```

---

## Phase 8: バックアップと復元

### 8.1 ローカルバックアップ

```bash
# ローカルに ZIP を作成
daid backup --local-only

# 期待結果: design-aid-backup_YYYYMMDD_HHMMSS.zip が作成される
```

### 8.2 バックアップファイルの確認

```bash
ls -la *.zip

# 期待結果: バックアップ ZIP ファイルが存在する
```

### 8.3 バックアップファイル名を記録

```bash
# ここで作成された ZIP ファイル名をメモする
# 例: design-aid-backup_20260205_120000.zip
```

---

## Phase 9: クリーンアップ（削除操作）

### 9.1 子装置リンクの解除

```bash
# 子装置のリンクを解除
daid asset unlink lifting-unit --child safety-module

# 期待結果: safety-module と lifting-unit のリンクが解除される
```

### 9.2 パーツの削除

```bash
# 確認プロンプトに y で回答
daid part remove BOLT-M10-30

daid part remove MTR-001

daid part remove BASE-PLATE-001

# 期待結果: パーツが削除される
```

### 9.3 パーツ一覧（空確認）

```bash
daid part list

# 期待結果: パーツがない
```

### 9.4 装置の削除

```bash
# 確認プロンプトに y で回答
daid asset remove lifting-unit

daid asset remove control-panel

daid asset remove safety-module

# 期待結果: 装置が削除される
```

### 9.5 装置一覧（空確認）

```bash
daid asset list

# 期待結果: 装置がない
```

---

## Phase 10: 復元テスト

### 10.1 バックアップから復元

```bash
# Phase 8.3 でメモした ZIP ファイル名を使用
# 確認プロンプトに y で回答
daid restore ./design-aid-backup_YYYYMMDD_HHMMSS.zip

# 期待結果: データが復元される
```

### 10.2 復元後のデータ確認

```bash
daid asset list

daid part list

# 期待結果:
# - 3つの装置が復元されている
# - 3つのパーツが復元されている
```

### 10.3 再クリーンアップ

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

## Phase 11: 完全クリーンアップ

### 11.1 バックアップファイルの削除

```bash
rm -f *.zip

# 期待結果: ZIP ファイルが削除される
```

### 11.2 data ディレクトリの削除

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
- [ ] Phase 7: archive asset/part/list/restore が正常動作
- [ ] Phase 8: backup が正常動作
- [ ] Phase 9: 全削除操作が正常動作
- [ ] Phase 10: restore が正常動作
- [ ] Phase 11: data/ ディレクトリが完全に削除可能

## 備考

- ベクトル検索は SQLite + HNSW で組み込み実装されており、外部サービス（Docker 等）は不要です
- ベクトル検索を利用するには `daid sync --include-vectors` でインデックスを構築してください
- AWS S3 バックアップは AWS CLI/プロファイルが設定されていない場合はスキップしてください
- 各コマンドのヘルプは `--help` オプションで確認できます
- 削除コマンド（asset remove, part remove）は確認プロンプトが表示されます

# Design Aid CLI テストシナリオ

このドキュメントは、Design Aid CLI (`daid`) の全コマンドをテストするためのシナリオを定義します。
空のプロジェクトディレクトリから開始し、全ての操作を実行後、ディレクトリを完全に削除して終了します。

## 前提条件

- .NET 10.0 SDK がインストールされていること
- `daid` がグローバルツールとしてインストールされていること、またはビルド済み DLL が利用可能なこと

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
```

## DLL 直接実行（開発用）

```bash
# ビルド
dotnet build

# テストディレクトリを作成して移動
mkdir .test-integration && cd .test-integration

# DLL を直接実行（dotnet run --project は CWD が変わるため非推奨）
dotnet ../src/DesignAid/bin/Debug/net10.0/daid.dll <command>
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

### 1.2 プロジェクトディレクトリの初期化（名前なし）

```bash
# テスト用ディレクトリを作成して移動
mkdir test-project && cd test-project

# setup コマンドを実行（カレントディレクトリを初期化）
daid setup

# 期待結果:
# - カレントディレクトリに assets/ が作成される
# - カレントディレクトリに components/ が作成される
# - カレントディレクトリに design_aid.db が作成される（マイグレーション適用）
# - Settings テーブルにデフォルト設定が書き込まれる
# - カレントディレクトリに .gitignore が作成される
```

### 1.2b プロジェクトディレクトリの初期化（名前付き）

```bash
# 名前付き setup（別テストとして実行）
daid setup my-project

# 期待結果:
# - my-project/ ディレクトリが作成される
# - my-project/assets/ が作成される
# - my-project/components/ が作成される
# - my-project/design_aid.db が作成される
```

### 1.3 設定の確認

```bash
# 設定を表示
daid config show

# パス情報を表示
daid config path

# 期待結果: 設定内容とパス情報が表示される
# - プロジェクトディレクトリがカレントディレクトリとして表示される
```

### 1.4 設定の変更

```bash
# ベクトル検索の有効/無効を確認
daid config show

# 設定を変更する場合
daid config set vector_search.enabled true

# 期待結果: vector_search.enabled が true になっている
```

### 1.5 上方向 DB 探索の確認

```bash
# assets/ ディレクトリから実行（1階層上に DB）
cd assets
daid asset list

# assets/xxx/ ディレクトリから実行（2階層上に DB）
mkdir test-dir && cd test-dir
daid asset list

# 期待結果: どちらもプロジェクトルートの DB を検出して正常動作

# DB がないディレクトリから実行
cd /tmp
daid asset list

# 期待結果: エラー（exit code 3）
# [ERROR] プロジェクトが見つかりません。
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
# - assets/lifting-unit/ ディレクトリが作成される
# - assets/lifting-unit/asset.json が作成される
# - **表示される ID が asset.json と DB で一致する**（v0.5.5-alpha 以降）
#   sqlite3 design_aid.db "SELECT Id FROM Assets WHERE Name='lifting-unit';"
#   と asset.json の "id" が同じであること
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
# - assets/lifting-unit/asset_links.json にリンク情報が保存される
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
# - components/BASE-PLATE-001/ ディレクトリが作成される
# - components/BASE-PLATE-001/part.json が作成される
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
# - assets/lifting-unit/asset_links.json にリンク情報が保存される
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
# - asset.json が揃っていれば [JSON MISSING] は出ない
```

### 4.3b asset.json 欠落の検出と復元

`asset list` は `asset.json` しか見ないため、DB にしか無い装置は**存在しないように見える**。
エラーが出ないので、検出できることを必ず確認する。

```bash
# 欠落を作る（古いバージョンで登録された装置と同じ状態）
rm assets/lifting-unit/asset.json          # Windows: Remove-Item assets\lifting-unit\asset.json

daid asset list
# 期待結果: **lifting-unit が一覧に出ない**

daid sync
# 期待結果:
# - [WARN] DB に行があるのに asset.json が無い装置が 1 件あります
# - [JSON MISSING] lifting-unit （Path と ID が表示される）
# - 「復元するには daid sync --restore-json」の案内が出る

daid sync --dry-run --restore-json
# 期待結果: 検出は出るが「(dry-run: 復元は行われません)」となり、**ファイルは作られない**

daid sync --restore-json
# 期待結果:
# - 「✓ 1 件の asset.json を DB の内容から復元しました」
# - assets/lifting-unit/asset.json が作られる
# - **その "id" が DB の Assets.Id と一致する**
# - "created_at" が UTC（末尾 Z）である

daid asset list
# 期待結果: lifting-unit が一覧に復帰する

daid sync
# 期待結果: [JSON MISSING] が出ない（冪等）
```

**既存ファイルを上書きしないことの確認:**

```bash
# asset.json の display_name を手で書き換えてから
daid sync --restore-json
# 期待結果: 検出 0 件。手で書いた値がそのまま残る（ファイル側が正本）
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
echo "テスト図面" > components/BASE-PLATE-001/drawing.dxf

# 同期して成果物を登録
daid sync

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
# - archive/assets/control-panel.zip が作成される
# - archive_index.json にエントリが追加される
# - assets/control-panel/ が削除される
# - 圧縮率が表示される
```

### 7.3 パーツをアーカイブ

```bash
# BOLT-M10-30 をアーカイブ
daid archive part BOLT-M10-30

# 期待結果:
# - archive/components/BOLT-M10-30.zip が作成される
# - archive_index.json にエントリが追加される
# - components/BOLT-M10-30/ が削除される
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
# - components/BOLT-M10-30/ が復元される
# - アーカイブファイルが削除される
# - archive_index.json からエントリが削除される
```

### 7.8 アーカイブから装置を復元

```bash
daid archive restore asset control-panel

# 期待結果:
# - assets/control-panel/ が復元される
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

# 期待結果: プロジェクトディレクトリにデータが復元される
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

## Phase 10b: MCP プロトコル疎通

**MCP の SDK を更新したら必ず実行する。**
daid はプロトコルを SDK に任せているため、**ビルドが通ることは「喋れている」ことを
何も保証しない**。MCP 仕様は `YYYY-MM-DD` 形式で後方非互換の改訂が入る。

```powershell
.\scripts\test-mcp.ps1

# 期待結果: 5 件すべて成功
# [OK] server/discover が 2026-07-28 を返す
# [OK] 新仕様で 13 ツール列挙
# [OK] 新仕様でツール実行が成功
# [OK] 旧 initialize が応答する
# [OK] 旧ハンドシェイクで 13 ツール列挙（後方互換）
```

手で確かめる場合の注意:

- **2026-07-28 のリクエストは `_meta` に `protocolVersion` と `clientCapabilities` の
  両方が必須。** 片方だけだと `-32602` で拒否される
- `server/discover` は初期化なしで呼べる（必須 RPC）
- サーバーが名乗るバージョンは csproj の `<Version>` 由来（`AppInfo.Version`）。
  **版上げ後にここが古いままなら、ハードコードが混入している**

---

## Phase 10c: 装置の改名（asset rename）

**ID を変えずに名前とフォルダを変えること、衝突では何も変えずに止まること**を確認する。
Phase 10 の後で装置もパーツも空の状態から始め、最後に再び空へ戻す。

### 10c.1 準備

```bash
daid asset add alpha --display-name "アルファ"
daid asset add beta
daid asset add gamma
daid part add RN-PART-001 --type Fabricated --name "改名試験用部品"
daid part link RN-PART-001 --asset alpha --quantity 2
# gamma が alpha を子装置として組み込む（asset_links.json に alpha が「名前で」書かれる）
daid asset link gamma --child alpha --quantity 1 --notes "改名試験"

# alpha に文書を置き、ベクトル索引に載せる（改名で文書チャンクの行が消え、再同期で新しい名前のキーで作り直されることの確認用）
echo "# alpha の設計書" > assets/alpha/DESIGN.md
daid sync --include-vectors
#   sqlite3 design_aid.db "SELECT COUNT(*) FROM VectorIndex WHERE Type='asset_doc' AND AssetName='alpha';" が 1 以上

# 改名前の ID を控える（asset.json と DB の両方）
#   assets/alpha/asset.json の "id"
#   sqlite3 design_aid.db "SELECT Id FROM Assets WHERE Name='alpha';"
```

### 10c.2 改名

```bash
daid asset rename alpha alpha2

# 期待結果（終了コード 0）:
# - "Asset renamed: alpha -> alpha2" と、ID・Path・PreviousNames: alpha が表示される
# - 終わりに「リモートの名前・URL は変えていません」と表示される
# - **ID が変わっていない**: assets/alpha2/asset.json の "id" と、DB の Assets.Id が、改名前の ID と同じ
# - assets/alpha/ は無く、assets/alpha2/ がある（.git ごと移っている: assets/alpha2/.git が存在する）
# - assets/alpha2/asset.json の "name" が alpha2、"display_name" は「アルファ」のまま（旧名と違うので変えない）、
#   "previous_names" が ["alpha"]
# - DB の Assets: Name=alpha2、DirectoryPath が assets/alpha2、PreviousNames が ["alpha"]
# - VectorIndex に AssetName='alpha' の行が残らず、alpha の asset_doc の行は消えている（再同期で作り直す）
```

### 10c.3 引き継ぎの確認

```bash
daid asset list --verbose
# 期待結果:
# - alpha2 が一覧に出る（alpha は出ない）
# - alpha2 に RN-PART-001 が x2 で紐づいたまま
# - gamma の子装置が alpha2 になっている（gamma の asset_links.json の child_asset_name が alpha2 に書き換わっている）

daid asset bom gamma --include-subassets
# 期待結果: 子装置 alpha2 の部品が BOM に出る（子装置の参照が切れていない）

daid sync
daid check
# 期待結果: エラーなし。sync の後も alpha2 の ID が同じ（DB の行が増えない: SELECT COUNT(*) FROM Assets が 3）

daid sync --include-vectors
# 期待結果: alpha2 の asset_doc の行が同数のまま（重複しない）。AssetName='alpha' の行は残らない
```

### 10c.4 衝突・不正は何も変えずに止まる

```bash
daid asset rename alpha2 beta     # 新名の装置が既にある
daid asset rename missing zzz     # 旧名の装置が無い
daid asset rename alpha2 alpha2   # 旧名と新名が同じ
daid asset rename alpha2 ALPHA2   # 大文字小文字だけの違い
daid asset rename alpha2 "a/b"    # 名前の検査に通らない
daid asset rename alpha2 CON      # OS の予約名

# 期待結果（すべて）:
# - 終了コード 1、stderr に [ERROR] と「何も変更していません。」
# - assets/ の中身・各 asset.json・DB の Assets が、実行前と同じ
```

```bash
# 移動先が既にフォルダ・ファイルとしてある場合
mkdir assets/blocked
daid asset rename alpha2 blocked
echo x > assets/blocked2 && daid asset rename alpha2 blocked2

# 期待結果: どちらも終了コード 1。assets/alpha2 は動かない
rmdir assets/blocked && rm assets/blocked2
```

### 10c.5 --json と --quiet

```bash
daid asset rename alpha2 alpha3 --json
# 期待結果: success=true、id が改名前と同じ、previousNames が ["alpha","alpha2"]、gitRemoteChanged=false の JSON

daid asset rename alpha3 alpha4 --quiet
# 期待結果: 標準出力に何も出ない（終了コード 0）。previous_names は ["alpha","alpha2","alpha3"]

daid asset rename alpha4 beta --json
# 期待結果: 終了コード 1、標準出力に success=false の JSON、stderr にエラー
```

### 10c.6 後始末

```bash
daid asset unlink gamma --child alpha4
daid part remove RN-PART-001      # y で回答
daid asset remove alpha4          # y で回答
daid asset remove beta            # y で回答
daid asset remove gamma           # y で回答
daid asset list

# 期待結果: 装置・パーツが空に戻る
```

---

## Phase 11: 完全クリーンアップ

### 11.1 バックアップファイルの削除

```bash
rm -f *.zip

# 期待結果: ZIP ファイルが削除される
```

### 11.2 テストディレクトリの削除

```bash
# テストディレクトリに戻って削除
cd ..
rm -rf test-project/

# ディレクトリが存在しないことを確認
ls -la test-project/ 2>&1 || echo "test directory removed successfully"

# 期待結果: "No such file or directory" または "test directory removed successfully"
```

---

## テスト完了チェックリスト

- [ ] Phase 1: setup（名前なし・名前付き）, config, 上方向 DB 探索が正常動作
- [ ] Phase 2: asset add/list/link/unlink が正常動作
- [ ] Phase 3: part add/list/link/remove が正常動作
- [ ] Phase 4: status, check, sync, verify が正常動作
- [ ] Phase 5: search が正常動作（または適切なエラー）
- [ ] Phase 6: deploy --dry-run が正常動作
- [ ] Phase 7: archive asset/part/list/restore が正常動作
- [ ] Phase 8: backup が正常動作
- [ ] Phase 9: 全削除操作が正常動作
- [ ] Phase 10: restore が正常動作
- [ ] Phase 10c: asset rename が ID を変えずに名前とフォルダを変え、衝突・不正では何も変えずに止まる
- [ ] Phase 11: テストディレクトリが完全に削除可能

## 備考

- プロジェクトルートは `design_aid.db` の存在で識別される
- コマンドはカレントディレクトリから上方向に最大2階層まで `design_aid.db` を探索する
- ベクトル検索は SQLite + HNSW で組み込み実装されており、外部サービス（Docker 等）は不要です
- ベクトル検索を利用するには `daid sync --include-vectors` でインデックスを構築してください
- AWS S3 バックアップは AWS CLI/プロファイルが設定されていない場合はスキップしてください
- 各コマンドのヘルプは `--help` オプションで確認できます
- 削除コマンド（asset remove, part remove）は確認プロンプトが表示されます
- `asset rename` は ID を変えない。改名後に旧名のフォルダ・asset.json は残らない

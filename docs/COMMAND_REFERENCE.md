# コマンドリファレンス

Design Aid CLI (`daid`) の全コマンド・オプション・設定キーの詳細リファレンス。

---

## 目次

- [グローバルオプション](#グローバルオプション)
- [環境変数](#環境変数)
- [終了コード](#終了コード)
- [コマンド一覧](#コマンド一覧)
  - [setup](#setup) — データディレクトリの初期化
  - [config](#config) — 設定管理
  - [asset](#asset) — 装置管理
  - [part](#part) — パーツ管理
  - [check](#check) — 整合性検証
  - [verify](#verify) — 設計基準バリデーション
  - [sync](#sync) — ファイルシステムと DB の同期
  - [status](#status) — システム状態表示
  - [deploy](#deploy) — 手配パッケージ作成
  - [search](#search) — 類似設計検索
  - [backup](#backup) — バックアップ
  - [restore](#restore) — 復元
  - [archive](#archive) — アーカイブ（容量節約）
  - [update](#update) — ツール更新
- [設定キー一覧](#設定キー一覧)
- [データ構造](#データ構造)
  - [ディレクトリ構成](#ディレクトリ構成)
  - [part.json スキーマ](#partjson-スキーマ)
  - [asset.json スキーマ](#assetjson-スキーマ)
- [パーツ種別](#パーツ種別)
- [手配ステータス](#手配ステータス)
- [設計基準（Standards）](#設計基準standards)
- [ファイルハッシュ形式](#ファイルハッシュ形式)

---

## グローバルオプション

| オプション | 説明 |
|-----------|------|
| `--help`, `-h`, `-?` | ヘルプを表示 |
| `--version` | バージョンを表示 |

---

## 環境変数

| 変数名 | 用途 | デフォルト |
|--------|------|-----------|
| `DA_DATA_DIR` | データディレクトリのパスを明示的に指定 | （後述の自動検出） |

**データディレクトリの検出順序:**

1. `DA_DATA_DIR` 環境変数（設定されている場合）
2. リポジトリルートの `data/`（`DesignAid.sln` を目印に探索）
3. カレントディレクトリの `./data`（フォールバック）

---

## 終了コード

| コード | 意味 | 説明 |
|--------|------|------|
| 0 | 成功 | 正常完了 |
| 1 | 一般エラー | 実行時エラー、バリデーション失敗 |
| 2 | 引数エラー | 不正なコマンドライン引数 |
| 3 | 設定エラー | DB 未作成、設定不正 |
| 4 | 接続エラー | 外部サービスの接続失敗 |
| 5 | 整合性エラー | ハッシュ不一致、データ破損 |

---

## コマンド一覧

### setup

データディレクトリを初期化し、DB とデフォルト設定を作成する。

```
daid setup [options]
```

| オプション | 説明 |
|-----------|------|
| `--path <path>` | データディレクトリのパスを指定（デフォルト: `./data`） |
| `--force` | 既存の設定をリセットしてデフォルト値で上書き |

**実行内容:**

1. `data/` ディレクトリ作成
2. `data/assets/` ディレクトリ作成
3. `data/components/` ディレクトリ作成
4. `data/.gitignore` 作成
5. `data/design_aid.db` 作成（EF Core マイグレーション適用）
6. 既存の `config.json` があれば Settings テーブルに自動移行
7. Settings テーブルにデフォルト設定を書き込み

**例:**

```bash
# 標準の初期化
daid setup

# カスタムパスで初期化
daid setup --path /home/user/my-project/data

# 設定をリセット
daid setup --force
```

---

### config

設定の表示・変更・パス確認を行う。全設定は SQLite の Settings テーブルに保存される。

#### config show

```
daid config show [options]
```

| オプション | 説明 |
|-----------|------|
| `--json` | JSON 形式で出力 |

**出力例:**

```
=== Design Aid 設定 ===

[database]
  database.path = design_aid.db

[vector_search]
  vector_search.enabled = true
  vector_search.hnsw_index_path = hnsw_index.bin

[embedding]
  embedding.provider = Mock
  embedding.dimensions = 384
  embedding.model = (未設定)
  embedding.api_key = ****
  embedding.endpoint = (未設定)

[hashing]
  hashing.algorithm = SHA256

[backup]
  backup.s3_bucket = (未設定)
  backup.s3_prefix = design-aid-backup/
  backup.aws_profile = default
```

> **注意:** `embedding.api_key` はマスクされて表示されます。

#### config set

```
daid config set <key> <value>
```

| 引数 | 説明 |
|------|------|
| `<key>` | 設定キー（[設定キー一覧](#設定キー一覧) 参照） |
| `<value>` | 設定値 |

既知のキーのみ受け付けます。不明なキーを指定するとエラーになります。

**例:**

```bash
# 埋め込みプロバイダーを変更
daid config set embedding.provider OpenAI

# API キーを設定
daid config set embedding.api_key sk-your-api-key

# ベクトル検索を無効化
daid config set vector_search.enabled false

# S3 バケットを設定
daid config set backup.s3_bucket my-backup-bucket
```

#### config path

```
daid config path
```

データディレクトリと DB ファイルのパスを表示する。

**出力例:**

```
Data directory: c:\Users\user\project\data
Database: c:\Users\user\project\data\design_aid.db
```

---

### asset

装置（Asset）の追加・一覧・削除・リンク管理を行う。

#### asset add

```
daid asset add <name> [options]
```

| 引数/オプション | 説明 |
|----------------|------|
| `<name>` | 装置名（英数字・ハイフン） |
| `--display-name <name>` | 表示名（日本語可） |
| `--description <text>` | 説明 |
| `--no-git` | git init をスキップ |

**実行内容:**

1. `data/assets/<name>/` ディレクトリ作成
2. `asset.json` 生成（UUID 自動付与）
3. DB にレコード挿入
4. `git init` 実行（`--no-git` で無効化）

**例:**

```bash
daid asset add lifting-unit --display-name "昇降ユニット"
daid asset add control-panel --display-name "制御盤" --description "メイン制御盤"
daid asset add test-fixture --no-git
```

#### asset list

```
daid asset list [options]
```

| オプション | 説明 |
|-----------|------|
| `--json` | JSON 形式で出力 |
| `--verbose` | 詳細表示（リンクされたパーツ・子装置を含む） |

#### asset remove

```
daid asset remove <name> [options]
```

| オプション | 説明 |
|-----------|------|
| `--force` | 確認プロンプトをスキップ |

#### asset link

子装置（SubAsset）を親装置に組み込む。

```
daid asset link <parent-asset> [options]
```

| オプション | 説明 |
|-----------|------|
| `--child <name>` | 子装置名（**必須**） |
| `--quantity <n>` | 数量（デフォルト: 1） |
| `--notes <text>` | 備考 |

**例:**

```bash
daid asset link lifting-unit --child safety-module --quantity 1 --notes "安全装置"
```

#### asset unlink

子装置のリンクを解除する。

```
daid asset unlink <parent-asset> [options]
```

| オプション | 説明 |
|-----------|------|
| `--child <name>` | 子装置名（**必須**） |

---

### part

パーツ（Part / Component）の追加・一覧・削除・装置リンクを行う。

#### part add

```
daid part add <part-number> [options]
```

| 引数/オプション | 説明 |
|----------------|------|
| `<part-number>` | 型式（例: `BASE-PLATE-001`） |
| `--name <name>` | パーツ名 |
| `--type <type>` | 種別: `Fabricated` / `Purchased` / `Standard` |
| `--material <material>` | 材質（メタデータに保存） |
| `--no-git` | git init をスキップ |

**実行内容:**

1. `data/components/<part-number>/` ディレクトリ作成
2. `part.json` 生成（UUID 自動付与、バージョン `1.0.0`）
3. DB にレコード挿入

**例:**

```bash
daid part add SP-2026-PLATE-01 --name "ベースプレート" --type Fabricated --material SS400
daid part add MTR-001 --name "サーボモーター" --type Purchased
daid part add BOLT-M10-30 --name "六角ボルト M10x30" --type Standard
```

#### part list

```
daid part list [options]
```

| オプション | 説明 |
|-----------|------|
| `--json` | JSON 形式で出力 |

#### part remove

```
daid part remove <part-number> [options]
```

| オプション | 説明 |
|-----------|------|
| `--force` | 確認プロンプトをスキップ |

#### part link

パーツを装置に紐付ける。

```
daid part link <part-number> [options]
```

| オプション | 説明 |
|-----------|------|
| `--asset <name>` | 装置名（**必須**） |
| `--quantity <n>` | 数量（デフォルト: 1） |

**例:**

```bash
daid part link BASE-PLATE-001 --asset lifting-unit --quantity 1
daid part link BOLT-M10-30 --asset lifting-unit --quantity 8
```

---

### check

全パーツの成果物（図面・計算書等）のファイルハッシュ整合性を検証する。

```
daid check [options]
```

| オプション | 説明 |
|-----------|------|
| `--path <path>` | データディレクトリのパス |
| `--json` | JSON 形式で出力 |
| `--verbose` | 詳細出力 |

**検証内容:**

| チェック項目 | 重要度 | 説明 |
|-------------|--------|------|
| 成果物の存在確認 | ERROR | `part.json` に記載されたファイルが実在するか |
| ハッシュ値の一致 | WARNING | ファイルのハッシュ値が `part.json` の記録と一致するか |
| 未登録ファイルの検出 | INFO | ディレクトリ内にあるが `part.json` に未登録のファイル |

**出力ステータス:**

- `OK` — 全チェック合格
- `WARNING` — ハッシュ不一致あり（ファイルは存在）
- `ERROR` — ファイル欠損またはハッシュ計算エラー

> ハッシュ不一致は `daid sync` で解消できます。

---

### verify

パーツが設計基準（Standards）に適合しているかを検証する。

```
daid verify [options]
```

| オプション | 説明 |
|-----------|------|
| `--part <part-number>` | 特定のパーツのみ検証 |
| `--standard <standard-id>` | 特定の設計基準のみ検証 |
| `--json` | JSON 形式で出力 |

**組み込み設計基準:**

| ID | 名称 | 対象 | 検証内容 |
|----|------|------|---------|
| `STD-MATERIAL-01` | 材質基準 | Fabricated のみ | メタデータの `material` を承認済み材質リストと照合 |
| `STD-TOLERANCE-02` | 公差基準 | — | 公差関連のメタデータを検証 |

**材質基準の詳細（STD-MATERIAL-01）:**

承認済み材質:

| カテゴリ | 材質 |
|---------|------|
| 構造用鋼 | SS400, SS490, SS540 |
| 機械構造用鋼 | S45C, S50C, S55C |
| ステンレス鋼 | SUS304, SUS316, SUS316L, SUS303, SUS430 |
| アルミニウム合金 | A5052, A6063, A7075 |
| 黄銅 | C3604 |

条件付き承認材質（技術審査要）:

| カテゴリ | 材質 |
|---------|------|
| 高強度アルミ | A2017, A2024 |
| 高硬度ステンレス | SUS440C |
| 工具鋼 | SKD11, SKD61 |
| クロムモリブデン鋼 | SCM435, SCM440 |

**例:**

```bash
# 全パーツを全基準で検証
daid verify

# 特定パーツのみ
daid verify --part BASE-PLATE-001

# 材質基準のみ
daid verify --standard STD-MATERIAL-01
```

---

### sync

ファイルシステムと DB の同期を行う。成果物のハッシュ値を再計算し、`part.json` を更新する。

```
daid sync [options]
```

| オプション | 説明 |
|-----------|------|
| `--dry-run` | 変更内容を表示するのみ（実際の変更なし） |
| `--include-vectors` | ベクトルインデックスも同期（検索用） |
| `--force` | 全パーツを強制的に再同期 |
| `--json` | JSON 形式で出力 |

**同期内容:**

1. **ファイル変更の検出**
   - 新規ファイル: ディレクトリにあるが `part.json` 未登録
   - 変更ファイル: ハッシュ値が `part.json` と不一致
   - 削除ファイル: `part.json` に記載あるがファイルなし

2. **ベクトルインデックス同期**（`--include-vectors` 時）
   - パーツ情報（名前・種別・メモ・メタデータ）からベクトル生成
   - `VectorIndex` テーブルに保存
   - HNSW グラフを再構築 → `hnsw_index.bin` に書き出し

**例:**

```bash
# 通常の同期
daid sync

# ドライラン（確認のみ）
daid sync --dry-run

# ベクトルインデックス付き同期
daid sync --include-vectors

# 全パーツを強制再同期
daid sync --force
```

---

### status

システムの状態を表示する。

```
daid status [options]
```

| オプション | 説明 |
|-----------|------|
| `--asset <name>` | 特定の装置の詳細を表示 |
| `--json` | JSON 形式で出力 |

**表示内容:**

| セクション | 情報 |
|-----------|------|
| System | データディレクトリパス、DB ファイルパス・存在有無 |
| Assets | 装置一覧（名前・ID・表示名） |
| Components | パーツ総数、種別ごとの内訳 |
| Vector Index | 状態（無効 / 未初期化 / 空 / ベクトル数・次元数） |

---

### deploy

手配（発注）パッケージを作成する。成果物のあるパーツを収集し、手配用のディレクトリを生成する。

```
daid deploy [options]
```

| オプション | 説明 |
|-----------|------|
| `--part <part-number>` | 特定のパーツのみデプロイ |
| `--output <path>` | 出力先ディレクトリ |
| `--json` | JSON 形式で出力 |
| `--no-confirm` | 確認プロンプトをスキップ |
| `--dry-run` | 対象の確認のみ（パッケージ作成なし） |

**デプロイ対象の条件:**

- `part.json` 以外の成果物ファイルが 1 つ以上存在すること
- パーツ種別（Fabricated / Purchased / Standard）は問わない

**生成されるパッケージ構成:**

```
deploy_YYYY-MM-DD_HHmmss/
├── BASE-PLATE-001/
│   ├── drawing.dxf
│   ├── calculation.pdf
│   └── part_info.txt          # パーツ情報サマリー
├── MTR-001/
│   ├── spec.pdf
│   └── part_info.txt
└── manifest.json              # マニフェスト（全パーツ情報）
```

**例:**

```bash
# ドライランで対象を確認
daid deploy --dry-run

# 手配パッケージを作成
daid deploy

# 特定パーツのみ
daid deploy --part BASE-PLATE-001

# 出力先を指定
daid deploy --output ./handover/2026-02-15
```

---

### search

類似設計をベクトル検索またはキーワード検索で探す。

```
daid search <query> [options]
```

| 引数/オプション | 説明 |
|----------------|------|
| `<query>` | 検索クエリ（**必須**） |
| `--threshold <value>` | 類似度閾値 0.0〜1.0（デフォルト: `0.7`） |
| `--top <n>` | 上位 N 件を表示（デフォルト: `10`） |
| `--json` | JSON 形式で出力 |
| `--local` | ローカルキーワード検索のみ使用 |

**検索モード:**

| モード | 条件 | 説明 |
|--------|------|------|
| ベクトル検索 | HNSW インデックスが構築済み | 意味的な類似度に基づく検索 |
| キーワード検索 | インデックス未構築 or `--local` | テキストマッチングによる検索 |

**キーワード検索のスコアリング:**

| フィールド | 重み | 説明 |
|-----------|------|------|
| `part_number` | 1.0 | 型式 |
| `name` | 1.0 | パーツ名 |
| `type` | 0.5 | 種別 |
| `memo` | 0.8 | メモ |
| `metadata.*` | 0.6 | メタデータの各キー・値 |

スコア = キーワードマッチ率 × 0.7 + 加重平均 × 0.3

**例:**

```bash
# ベクトル検索（要: sync --include-vectors 済み）
daid search "油圧シリンダ"

# 閾値を下げて幅広く検索
daid search "プレート" --threshold 0.3 --top 20

# キーワード検索を明示的に使用
daid search "モーター" --local

# JSON で出力
daid search "ベースプレート" --json
```

---

### backup

データディレクトリをバックアップする。SQLite DB のクリーンバックアップとデータファイルを ZIP に圧縮し、ローカル保存または S3 にアップロードする。

```
daid backup [options]
```

| オプション | 説明 |
|-----------|------|
| `--data-path <path>`, `-d` | バックアップ対象のデータディレクトリ |
| `--bucket <name>`, `-b` | S3 バケット名（省略時: DB 設定） |
| `--prefix <prefix>` | S3 プレフィックス（省略時: DB 設定） |
| `--profile <name>`, `-p` | AWS CLI プロファイル名（省略時: DB 設定） |
| `--output <path>`, `-o` | 出力先 ZIP ファイルパス |
| `--dry-run` | 実行内容を表示するのみ |
| `--local-only`, `-l` | ローカルに ZIP を作成するのみ（S3 にアップロードしない） |
| `--skip-db` | SQLite バックアップをスキップ（ファイルコピーのみ） |

**バックアップ内容:**

1. SQLite DB の `VACUUM INTO` によるクリーンバックアップ（ロックなし）
2. データファイル（assets/, components/ 等）のコピー
3. ZIP 圧縮

**S3 アップロード:**

S3 関連の設定は `daid config set` で事前に設定するか、オプションで指定する。

```bash
# 事前に設定
daid config set backup.s3_bucket my-backup-bucket
daid config set backup.s3_prefix design-aid/
daid config set backup.aws_profile my-profile
```

**例:**

```bash
# ローカルのみ
daid backup --local-only

# S3 にアップロード
daid backup

# 出力先を指定
daid backup --local-only --output ./backups/latest.zip

# ドライラン
daid backup --dry-run
```

---

### restore

バックアップファイル（ローカル ZIP または S3）からデータを復元する。

```
daid restore <source> [options]
```

| 引数/オプション | 説明 |
|----------------|------|
| `<source>` | ZIP ファイルパスまたは S3 URI（**必須**） |
| `--data-path <path>` | 復元先のデータディレクトリ |
| `--profile <name>` | AWS CLI プロファイル名 |
| `--force` | 確認プロンプトをスキップ |
| `--dry-run` | 復元内容を表示するのみ |

**例:**

```bash
# ローカル ZIP から復元
daid restore ./design-aid-backup_20260215_120000.zip

# S3 から復元
daid restore s3://my-bucket/design-aid-backup/latest.zip --profile my-profile

# 強制的に上書き復元
daid restore ./backup.zip --force
```

---

### archive

使用頻度の低い装置・パーツを ZIP 圧縮して容量を節約する。アーカイブしたアイテムは一覧から非表示になるが、復元可能。

#### archive asset

```
daid archive asset <name>
```

装置をアーカイブする。`data/archive/assets/<name>.zip` に圧縮し、元のディレクトリを削除する。

#### archive part

```
daid archive part <part-number>
```

パーツをアーカイブする。`data/archive/components/<part-number>.zip` に圧縮し、元のディレクトリを削除する。

#### archive list

```
daid archive list [options]
```

| オプション | 説明 |
|-----------|------|
| `--json` | JSON 形式で出力 |

アーカイブされたアイテムの一覧を表示する。圧縮前のサイズ、圧縮後のサイズ、節約量が表示される。

#### archive restore

```
daid archive restore asset <name>
daid archive restore part <part-number>
```

アーカイブから復元する。ZIP を展開し、元のディレクトリ構成に戻す。アーカイブファイルは削除される。

**例:**

```bash
# 装置をアーカイブ
daid archive asset old-project

# パーツをアーカイブ
daid archive part OLD-PART-001

# アーカイブ一覧
daid archive list

# 復元
daid archive restore asset old-project
daid archive restore part OLD-PART-001
```

---

### update

Design Aid CLI ツールを最新版に更新する。

```
daid update [options]
```

| オプション | 説明 |
|-----------|------|
| `--check` | 更新確認のみ（更新は実行しない） |
| `--force` | 確認なしで更新 |

---

## 設定キー一覧

`daid config set <key> <value>` で設定可能な全キー。

### database

| キー | デフォルト | 説明 |
|------|-----------|------|
| `database.path` | `design_aid.db` | DB ファイル名（データディレクトリからの相対パス） |

### vector_search

| キー | デフォルト | 説明 |
|------|-----------|------|
| `vector_search.enabled` | `true` | ベクトル検索の有効/無効（`true` / `false`） |
| `vector_search.hnsw_index_path` | `hnsw_index.bin` | HNSW インデックスファイルのパス |

### embedding

| キー | デフォルト | 説明 |
|------|-----------|------|
| `embedding.provider` | `Mock` | 埋め込みプロバイダー名（`Mock` / `OpenAI` 等） |
| `embedding.dimensions` | `384` | ベクトル次元数 |
| `embedding.model` | *(未設定)* | 使用するモデル名 |
| `embedding.api_key` | *(未設定)* | API キー |
| `embedding.endpoint` | *(未設定)* | エンドポイント URL |

### hashing

| キー | デフォルト | 説明 |
|------|-----------|------|
| `hashing.algorithm` | `SHA256` | ハッシュアルゴリズム |

### backup

| キー | デフォルト | 説明 |
|------|-----------|------|
| `backup.s3_bucket` | *(空)* | S3 バケット名 |
| `backup.s3_prefix` | `design-aid-backup/` | S3 プレフィックス |
| `backup.aws_profile` | `default` | AWS CLI プロファイル名 |

---

## データ構造

### ディレクトリ構成

```
data/
├── design_aid.db                    # SQLite データベース
├── hnsw_index.bin                   # HNSW ベクトルインデックス（再構築可能）
├── .gitignore
├── assets/                          # 装置
│   ├── lifting-unit/
│   │   └── asset.json
│   └── control-panel/
│       └── asset.json
├── components/                      # パーツ（手配境界の最小単位）
│   ├── BASE-PLATE-001/
│   │   ├── part.json                # パーツ定義
│   │   ├── drawing.dxf              # 成果物（図面）
│   │   └── calculation.pdf          # 成果物（計算書）
│   └── MTR-001/
│       ├── part.json
│       └── spec.pdf
└── archive/                         # アーカイブ済みアイテム
    ├── archive_index.json
    ├── assets/
    │   └── old-unit.zip
    └── components/
        └── OLD-PART-001.zip
```

### part.json スキーマ

```json
{
  "id": "770e8400-e29b-41d4-a716-446655440002",
  "part_number": "SP-2026-PLATE-01",
  "name": "昇降ベースプレート",
  "type": "Fabricated",
  "version": "1.0.0",
  "artifacts": [
    {
      "path": "drawing.dxf",
      "hash": "sha256:abc123def456..."
    }
  ],
  "standards": ["STD-MATERIAL-01"],
  "metadata": {
    "material": "SS400",
    "surface_treatment": "メッキ",
    "lead_time_days": 14
  },
  "memo": "前回のプロジェクトより剛性を20%強化"
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `id` | UUID | ○ | 一意識別子（自動生成） |
| `part_number` | string | ○ | 型式（グローバルに一意） |
| `name` | string | ○ | パーツ名 |
| `type` | string | ○ | `Fabricated` / `Purchased` / `Standard` |
| `version` | string | ○ | セマンティックバージョニング（初期値: `1.0.0`） |
| `artifacts` | array | — | 成果物一覧 |
| `artifacts[].path` | string | ○ | パーツディレクトリからの相対パス |
| `artifacts[].hash` | string | ○ | SHA256 ハッシュ値（`sha256:` プレフィックス付き） |
| `standards` | array | — | 適用する設計基準 ID の一覧 |
| `metadata` | object | — | カスタムメタデータ（キー・値ペア） |
| `memo` | string | — | メモ・備考 |

### asset.json スキーマ

```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "name": "lifting-unit",
  "display_name": "昇降ユニット",
  "description": "エレベータ更新案件の昇降機構",
  "created_at": "2026-02-02T10:30:00Z"
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `id` | UUID | ○ | 一意識別子（自動生成） |
| `name` | string | ○ | 装置名（英数字・ハイフン） |
| `display_name` | string | — | 表示名（日本語可） |
| `description` | string | — | 説明 |
| `created_at` | DateTime | ○ | 作成日時（ISO 8601 UTC） |

---

## パーツ種別

| 種別 | 説明 | 例 |
|------|------|----|
| `Fabricated` | 製作物（図面品） | ベースプレート、ブラケット |
| `Purchased` | 購入品 | サーボモーター、センサー |
| `Standard` | 規格品 | 六角ボルト、ベアリング |

---

## 手配ステータス

パーツのライフサイクルを管理するステータス。

| ステータス | 説明 | 変更可 | 遷移先 |
|-----------|------|--------|--------|
| `Draft` | 設計中（未手配） | ○ | Ordered, Canceled |
| `Ordered` | 手配済み | × | Delivered, Canceled |
| `Delivered` | 納品済み | × | — |
| `Canceled` | キャンセル | × | — |

> `Draft` 以外のステータスでは成果物の変更はできません。

---

## 設計基準（Standards）

### STD-MATERIAL-01（材質基準）

- **対象**: Fabricated パーツのみ
- **検証内容**: `metadata.material` の値を承認済み材質リストと照合
- **結果**:
  - 承認済み材質 → PASS
  - 条件付き承認材質 → WARNING（技術審査要）
  - 未知の材質 → FAIL

### STD-TOLERANCE-02（公差基準）

- 公差関連のメタデータを検証

---

## ファイルハッシュ形式

成果物のハッシュ値は以下の形式で管理される。

```
sha256:abc123def456789012345678901234567890123456789012345678901234
```

| 項目 | 値 |
|------|-----|
| プレフィックス | `sha256:` |
| ハッシュ長 | 64 文字（16進数） |
| 全体長 | 71 文字 |
| 正規表現 | `^sha256:[a-fA-F0-9]{64}$` |

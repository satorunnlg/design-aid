# コントリビューションガイド

Design Aid への貢献を歓迎します！このドキュメントでは、プロジェクトへの貢献方法を説明します。

## 行動規範

このプロジェクトに参加する際は、敬意を持ってコミュニケーションを取り、建設的なフィードバックを心がけてください。

## 貢献の方法

### バグ報告

1. [GitHub Issues](https://github.com/satorunnlg/design-aid/issues) で既存の Issue を確認
2. 新しいバグの場合は、Issue を作成
   - 再現手順を明記
   - 期待される動作と実際の動作を説明
   - 環境情報（OS、.NET バージョン等）を記載

### 機能要望

1. [GitHub Discussions](https://github.com/satorunnlg/design-aid/discussions) で議論を開始
2. 合意が得られたら Issue を作成

### プルリクエスト

1. リポジトリをフォーク
2. 機能ブランチを作成: `git checkout -b feature/my-feature`
3. 変更をコミット: `git commit -m "feat: 新機能の説明"`
4. プッシュ: `git push origin feature/my-feature`
5. プルリクエストを作成

## 開発環境のセットアップ

### 前提条件

- .NET 10.0 SDK
- Git
- Docker（オプション、Qdrant 使用時）

### ビルドと実行

```bash
# リポジトリのクローン
git clone https://github.com/your-username/design-aid.git
cd design-aid

# ビルド
dotnet build

# テスト
dotnet test

# CLI 統合テスト（Windows）
.\scripts\test-all.ps1

# CLI 統合テスト（Linux/macOS）
./scripts/test-all.sh
```

## コーディング規約

### 言語

- **コード**: C# 13
- **コメント**: 日本語
- **ドキュメント**: 日本語

### スタイル

- `dotnet format` でフォーマットを統一
- DESIGN.md の設計方針に従う
- 既存のコードスタイルを維持

### コミットメッセージ

[Conventional Commits](https://www.conventionalcommits.org/) に従う：

```
<type>: <description>

[optional body]
```

**type の種類:**

| type | 説明 |
|------|------|
| `feat` | 新機能 |
| `fix` | バグ修正 |
| `docs` | ドキュメントのみの変更 |
| `style` | コードの意味に影響しない変更（フォーマット等） |
| `refactor` | バグ修正でも機能追加でもないコード変更 |
| `test` | テストの追加・修正 |
| `chore` | ビルドプロセスやツールの変更 |

### テスト

新しい機能を追加する場合：

1. **ユニットテスト**: `tests/` ディレクトリに xUnit テストを追加
2. **CLI テスト**: `docs/TEST_SCENARIO.md` にシナリオを追加
3. **テストスクリプト更新**: `scripts/test-all.ps1` と `scripts/test-all.sh` を更新

詳細は [DESIGN.md のテスト戦略セクション](./DESIGN.md#テスト戦略) を参照。

## プルリクエストのチェックリスト

- [ ] ビルドが成功する
- [ ] 全てのテストがパスする
- [ ] DESIGN.md の方針に従っている
- [ ] 必要に応じてドキュメントを更新
- [ ] コミットメッセージが Conventional Commits に従っている

## 質問・ヘルプ

質問がある場合は [GitHub Discussions](https://github.com/satorunnlg/design-aid/discussions) で気軽に質問してください。

## ライセンス

貢献したコードは [MIT License](./LICENSE) のもとで公開されます。

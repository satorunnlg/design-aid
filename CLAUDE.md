# Project Rules: design-aid

Scope: This folder only.

## Language Policy (CRITICAL)

**Internal Processing**: English for analysis/reasoning is OK.

**Output (MANDATORY)**:
- User responses: Japanese only
- Code comments: Japanese only
- Design docs (DESIGN.md): Japanese only (MANDATORY)
- README files: Japanese only (MANDATORY)
- Documentation: Japanese only
- Explanations: Japanese only
- Technical terms (API/function names): English OK, but explain in Japanese

**Enforcement**: If output is in English, user says "日本語で" to correct.

## Work Process (MANDATORY)

### 1. Pre-Work
- Read `./DESIGN.md` fully before starting
- Verify task is within this project scope
- Never start without reading DESIGN.md
- Check `../DESIGN.md` if it exists (for parent project context)

### 2. During Work
- Follow all methods/procedures in DESIGN.md
- Minimal changes only
- Keep existing code style
- For cross-project changes, ask user first

### 3. Document Management (CRITICAL)

**Never create new documents without explicit user request:**
- Document = Any file for human reading (README, TODO, notes, guides, proposals, etc.)
- Code files (source, config, tests) are NOT documents

**Before creating any document:**
1. **Check all existing files** in project using file listing/search
2. **Use existing files** when possible:
   - Add to DESIGN.md for design/architecture decisions
   - Add to README for user-facing documentation
   - Add to existing notes/docs if applicable
3. **Only if absolutely necessary** and better for project management:
   - **Ask user first** before creating new document
   - Explain why existing files are insufficient
   - Get explicit approval

**This applies to ALL documents including:**
- TODO lists, task tracking files
- Additional README files
- Documentation files (*.md, *.txt)
- Design proposals, RFCs
- Meeting notes, decision logs

### 4. Design Sync (CRITICAL)
- Update `./DESIGN.md` when changing implementation/structure/procedures
- Report design-code inconsistencies before proceeding
- For ambiguous specs, propose DESIGN.md updates first

### 5. Completion Verification (CRITICAL)

機能実装・変更の完了時に、以下を**必ず**すべて実行すること：

- **Build**: `dotnet build` を実行し、警告・エラーが 0 であることを確認
- **Unit Test**: `dotnet test` を実行し、全テストがパスすることを確認（pass/fail 数を報告）
- **CLI Integration Test**: `docs/TEST_SCENARIO.md` に定義された全テストシナリオ（Phase 1〜11）を実行し、各フェーズの結果を報告
  - テストシナリオは空の data ディレクトリから開始し、全操作を実行後、再び空の data ディレクトリに戻ることを検証
  - 失敗したフェーズがある場合は、修正してから再実行
- **Review**: DESIGN.md updated, change rationale documented

**Resource Cleanup**:
- Stop all processes (servers, daemons, Docker containers, etc.)
- Release ports, file locks, handles
- Follow DESIGN.md for proper shutdown method

### 6. Breaking Changes
- Show impact analysis first
- Get user confirmation before proceeding
- Update DESIGN.md with migration notes if needed

## Development Flow (MANDATORY)

If DESIGN.md defines a development flow, follow it strictly.
If not defined, use standard development practices with these minimums:
- Write tests for new functionality
- Verify build succeeds before commit
- Update documentation for significant changes

## .NET Rules (MANDATORY)

- Always use `dotnet new` to create projects and solutions
- Never manually create .csproj or .sln files
- Use `dotnet sln add` to add projects to solution
- Use xUnit for testing
- Use `dotnet format` for code formatting

**Dependency version management**:
1. Install without version: `dotnet add package PackageName`
2. Check installed version: `dotnet list package` or check .csproj file
3. Record exact version in .csproj: `<PackageReference Include="PackageName" Version="X.Y.Z" />`
4. Example:
   ```bash
   dotnet add package Microsoft.EntityFrameworkCore.Sqlite
   dotnet list package  # Shows installed version
   # Verify .csproj contains exact version
   ```

## CLI Application Rules (MANDATORY)

### コマンドライン設計
- `System.CommandLine` または `Spectre.Console.Cli` を使用
- サブコマンド構造: `daid <command> [options]`
- ヘルプは自動生成（`--help` / `-h`）
- バージョン表示: `--version` / `-v`

### 出力フォーマット
- 通常出力: 人間が読みやすい形式
- `--json` オプション: JSON形式（スクリプト連携用）
- `--quiet` / `-q`: 最小出力
- `--verbose` / `-v`: 詳細出力

### エラーハンドリング
- 終了コード: 0=成功, 1=一般エラー, 2=引数エラー
- エラーメッセージは stderr に出力
- ユーザーフレンドリーなエラーメッセージ + 詳細は `--verbose` で

## SQLite Rules (MANDATORY)

### Entity Framework Core 使用
- `Microsoft.EntityFrameworkCore.Sqlite` を使用
- マイグレーション: `dotnet ef migrations add <Name>`
- DB更新: `dotnet ef database update`

### データベース管理
- DB ファイルはプロジェクトルートまたは指定パスに配置
- 接続文字列は設定ファイルまたは環境変数で管理
- トランザクションを適切に使用

### スキーマ変更
- マイグレーションファイルはリポジトリに含める
- 破壊的変更時はデータ移行スクリプトを作成

## Vector Search Rules (MANDATORY)

### アーキテクチャ
- SQLite BLOB によるベクトル永続化 + HNSW ライブラリによるインメモリ ANN 検索
- 外部依存（Docker / Qdrant）は不要
- `HNSW` NuGet パッケージ (curiosity-ai) を使用

### ベクトル管理
- VectorIndex テーブル（SQLite）にベクトルを BLOB として保存
- HNSW インデックスファイル (`hnsw_index.bin`) はキャッシュ（再構築可能）
- 埋め込みモデル: OpenAI text-embedding-ada-002 (1536次元) をデフォルト想定
- メタデータ: `part_id`, `asset_id`, `type` 等を付与

### インデックス管理
- `daid sync --include-vectors` でベクトルインデックスを構築・再構築
- HNSW パラメータ: M=16, 距離関数=CosineDistance.SIMDForUnits
- インデックスファイルは .gitignore に含める（再構築可能なため）

## 設計哲学の遵守 (CRITICAL)

本システムは以下の設計哲学に基づく。実装時は常にこれを念頭に置くこと：

### Support, Not Control
- 設計者を縛るのではなく、ミスの検知や過去の知見の提示を通じて「助ける」存在
- 警告は出すが、最終判断は設計者に委ねる
- 過度な制約や強制は避ける

### Procurement Boundary (手配境界)
- 自社から他社へ手配（発注）するタイミングをシステムの境界とする
- `part.json` + 成果物（図面・計算書）が1つのパーツの最小単位
- 手配ステータス管理が中心機能

### Hash-Based Integrity
- 全ての成果物（図面・計算書）をハッシュ値で管理
- ファイルとハッシュの不整合を検知
- 手配後の変更を追跡可能に

## GUI Policy (将来対応)

将来 GUI を追加する場合:

**GUI_POLICY.md is the absolute standard for all UI/UX design.**

### Pre-Work for GUI Changes
- Read `./GUI_POLICY.md` fully before any GUI-related work
- If GUI_POLICY.md does not exist, request user to create it first
- Never implement GUI without GUI_POLICY.md

### During GUI Work
- Follow all design specifications in GUI_POLICY.md strictly
- Use defined color palette, typography, spacing, and components
- Maintain consistency with existing UI elements
- If design decision is not covered in GUI_POLICY.md, ask user first

### Design Sync
- Update GUI_POLICY.md when adding new components or patterns
- Report GUI_POLICY.md violations before proceeding
- Propose GUI_POLICY.md updates for new design requirements

### Avalonia UI 使用時
- MVVM パターンを使用
- `CommunityToolkit.Mvvm` を使用
- AXAML でUI定義

## License Management (MANDATORY)

When adding dependencies/libraries:
- Track all dependency licenses
- Record license info in DESIGN.md or dedicated license file
- Check license compatibility with project license

If application displays license info (e.g., "About" command, OSS notice):
- Include mechanism to collect and display dependency licenses
- Keep license display updated when dependencies change
- Follow each license's attribution requirements

## OS-Aware Command Execution (CRITICAL)

Before running any shell command:
- **Check current OS** using appropriate detection method
- **Use OS-appropriate commands**: Windows (cmd/PowerShell) vs Unix (bash/sh)
- **Path separators**: `\` for Windows, `/` for Unix
- **Null device**: `NUL` for Windows, `/dev/null` for Unix
- **Common differences**:
  - Directory listing: `dir` (Windows) vs `ls` (Unix)
  - Clear screen: `cls` (Windows) vs `clear` (Unix)
  - Environment variables: `%VAR%` (cmd), `$env:VAR` (PowerShell) vs `$VAR` (Unix)
  - Command chaining: `&&` works on both, but `;` only on Unix

**WARNING**: Redirecting to `nul` on Windows in some contexts creates a file named "nul" instead of discarding output. Use proper null device handling.

## File Safety
- Never create: `nul`, `con`, `prn`, `aux`, `com1-9`, `lpt1-9`
- Delete if accidentally created
- Avoid OS reserved names

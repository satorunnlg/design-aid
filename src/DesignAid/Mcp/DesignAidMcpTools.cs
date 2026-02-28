using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using DesignAid.Application.Services;
using DesignAid.Commands;
using DesignAid.Domain.Entities;
using DesignAid.Infrastructure.Embedding;

namespace DesignAid.Mcp;

/// <summary>
/// Design Aid MCP ツール定義。
/// 既存の Application Layer サービスを MCP ツールとして公開する。
/// </summary>
[McpServerToolType]
public static class DesignAidMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ========================================
    // 装置（Asset）管理
    // ========================================

    [McpServerTool, Description("装置（アセット）一覧を取得する。status で active/dormant/archived/third_party をフィルタ可能。")]
    public static async Task<string> ListAssets(
        IAssetService assetService,
        [Description("ステータスフィルタ (active/dormant/archived/third_party)。省略時は全件。")] string? status = null,
        [Description("タグフィルタ。省略時は全件。")] string? tag = null)
    {
        var assets = await assetService.GetAllAsync();

        // ステータスフィルタ
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AssetStatus>(status, ignoreCase: true, out var statusEnum))
        {
            assets = assets.Where(a => a.Status == statusEnum).ToList();
        }

        // タグフィルタ
        if (!string.IsNullOrEmpty(tag))
        {
            assets = assets.Where(a => a.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        var result = assets.Select(a => new
        {
            a.Name,
            a.DisplayName,
            a.Id,
            Status = a.Status.ToString(),
            a.Description,
            a.DirectoryPath
        }).OrderBy(a => a.Name);

        return JsonSerializer.Serialize(new { assets = result, total = assets.Count }, JsonOptions);
    }

    [McpServerTool, Description("新しい装置（アセット）を追加する。")]
    public static async Task<string> AddAsset(
        IAssetService assetService,
        [Description("装置名（ディレクトリ名にもなる）")] string name,
        [Description("表示名。省略時は name と同じ。")] string? displayName = null,
        [Description("装置の説明。")] string? description = null)
    {
        var assetsDir = CommandHelper.GetAssetsDirectory();
        var assetPath = Path.Combine(assetsDir, name);

        if (!Directory.Exists(assetPath))
        {
            Directory.CreateDirectory(assetPath);
        }

        var asset = await assetService.AddAsync(name, assetPath, displayName, description);

        return JsonSerializer.Serialize(new
        {
            success = true,
            asset = new { asset.Name, asset.DisplayName, asset.Id, asset.DirectoryPath }
        }, JsonOptions);
    }

    // ========================================
    // パーツ管理
    // ========================================

    [McpServerTool, Description("パーツ一覧を取得する。")]
    public static async Task<string> ListParts(IPartService partService)
    {
        var parts = await partService.GetAllAsync();

        var result = parts.Select(p => new
        {
            PartNumber = p.PartNumber.ToString(),
            p.Name,
            Type = p.GetType().Name,
            p.Memo,
            p.DirectoryPath
        }).OrderBy(p => p.PartNumber);

        return JsonSerializer.Serialize(new { parts = result, total = parts.Count() }, JsonOptions);
    }

    [McpServerTool, Description("パーツの詳細情報を取得する。")]
    public static async Task<string> GetPartDetails(
        IPartService partService,
        [Description("パーツ型式番号")] string partNumber)
    {
        var part = await partService.GetByPartNumberAsync(partNumber);
        if (part == null)
            return JsonSerializer.Serialize(new { error = $"パーツ '{partNumber}' が見つかりません" }, JsonOptions);

        return JsonSerializer.Serialize(new
        {
            PartNumber = part.PartNumber.ToString(),
            part.Name,
            Type = part.GetType().Name,
            part.Memo,
            part.DirectoryPath,
            part.Id,
            part.CreatedAt
        }, JsonOptions);
    }

    [McpServerTool, Description("パーツを装置にリンクする。")]
    public static async Task<string> LinkPartToAsset(
        IPartService partService,
        IAssetService assetService,
        [Description("パーツ型式番号")] string partNumber,
        [Description("装置名")] string assetName,
        [Description("数量")] int quantity = 1)
    {
        var part = await partService.GetByPartNumberAsync(partNumber);
        if (part == null)
            return JsonSerializer.Serialize(new { error = $"パーツ '{partNumber}' が見つかりません" }, JsonOptions);

        var asset = await assetService.GetByNameAsync(assetName);
        if (asset == null)
            return JsonSerializer.Serialize(new { error = $"装置 '{assetName}' が見つかりません" }, JsonOptions);

        var link = await partService.LinkToAssetAsync(asset.Id, part.Id, quantity);

        return JsonSerializer.Serialize(new
        {
            success = true,
            link = new { partNumber, assetName, quantity }
        }, JsonOptions);
    }

    // ========================================
    // 整合性・検証
    // ========================================

    [McpServerTool, Description("ファイルハッシュの整合性を検証する。不整合があるパーツを報告する。")]
    public static async Task<string> CheckIntegrity(
        IHashService hashService,
        IPartService partService)
    {
        var parts = await partService.GetAllAsync();
        var results = new List<object>();

        foreach (var part in parts)
        {
            var validation = hashService.ValidateComponentIntegrity(part);
            if (!validation.IsSuccess)
            {
                results.Add(new
                {
                    PartNumber = part.PartNumber.ToString(),
                    part.Name,
                    validation.Message,
                    Issues = validation.Details.Select(d => $"[{d.Severity}] {d.Field}: {d.Message}")
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            totalParts = parts.Count,
            issueCount = results.Count,
            issues = results
        }, JsonOptions);
    }

    [McpServerTool, Description("設計基準に基づくバリデーションを実行する。")]
    public static async Task<string> VerifyStandards(
        IValidationService validationService,
        [Description("検証対象の設計基準ID。省略時は全基準。")] string? standardId = null)
    {
        var result = await validationService.VerifyAllAsync(standardId);

        return JsonSerializer.Serialize(new
        {
            totalChecked = result.Results.Count,
            passCount = result.PassCount,
            warningCount = result.WarningCount,
            failCount = result.FailCount,
            results = result.Results.Select(r => new
            {
                partNumber = r.Key,
                validations = r.Value.Select(v => new
                {
                    v.Severity,
                    v.Message,
                    v.Target
                })
            })
        }, JsonOptions);
    }

    // ========================================
    // 装置のパーツ情報
    // ========================================

    [McpServerTool, Description("指定した装置に紐づくパーツ情報を取得する。")]
    public static async Task<string> GetAssetParts(
        IAssetService assetService,
        IPartService partService,
        [Description("装置名")] string assetName)
    {
        var asset = await assetService.GetByNameAsync(assetName);
        if (asset == null)
            return JsonSerializer.Serialize(new { error = $"装置 '{assetName}' が見つかりません" }, JsonOptions);

        var partsWithLinks = await partService.GetPartsByAssetAsync(asset.Id);

        var result = partsWithLinks.Select(pl => new
        {
            pl.Part.PartNumber,
            pl.Part.Name,
            Type = pl.Part.GetType().Name,
            pl.Link.Quantity,
            pl.Link.Notes
        });

        return JsonSerializer.Serialize(new
        {
            assetName = asset.Name,
            parts = result,
            total = partsWithLinks.Count
        }, JsonOptions);
    }

    // ========================================
    // 検索
    // ========================================

    [McpServerTool, Description("類似設計をベクトル検索する。")]
    public static async Task<string> SearchDesigns(
        ISearchService searchService,
        [Description("検索クエリ")] string query,
        [Description("類似度閾値 (0.0-1.0)。デフォルト 0.7。")] double threshold = 0.7,
        [Description("取得件数上限。デフォルト 10。")] int limit = 10)
    {
        var isAvailable = await searchService.IsVectorIndexAvailableAsync();
        if (!isAvailable)
            return JsonSerializer.Serialize(new { error = "ベクトルインデックスが構築されていません。`daid sync --include-vectors` を実行してください。" }, JsonOptions);

        try
        {
            var searchResults = await searchService.SearchAsync(query, threshold, limit);

            return JsonSerializer.Serialize(new
            {
                query,
                threshold,
                hits = searchResults.Select(r => new
                {
                    r.PartNumber,
                    r.AssetName,
                    r.ProjectName,
                    r.Score,
                    r.Content,
                    r.FilePath
                }),
                total = searchResults.Count
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, type = ex.GetType().Name }, JsonOptions);
        }
    }

    // ========================================
    // 同期
    // ========================================

    [McpServerTool, Description("ファイルシステムとDBを同期する。")]
    public static async Task<string> SyncAssets(
        ISyncService syncService,
        [Description("ベクトルインデックスも同期する場合 true")] bool includeVectors = false,
        [Description("強制同期（ハッシュ再計算）する場合 true")] bool force = false)
    {
        var result = await syncService.SyncAllAsync(force, includeVectors);

        return JsonSerializer.Serialize(new
        {
            success = true,
            updatedParts = result.Updated.Count,
            errors = result.Errors.Count,
            vectorSyncCount = result.VectorSyncCount,
            updatedDetails = result.Updated.Select(u => new { partNumber = u.Key, change = u.Value.ToString() }),
            errorDetails = result.Errors
        }, JsonOptions);
    }

    // ========================================
    // システム状態
    // ========================================

    [McpServerTool, Description("Design Aid システムの現在の状態を取得する。")]
    public static async Task<string> GetStatus(
        IAssetService assetService,
        IPartService partService,
        ISearchService searchService)
    {
        var assets = await assetService.GetAllAsync();
        var parts = await partService.GetAllAsync();
        var vectorAvailable = await searchService.IsVectorIndexAvailableAsync();

        VectorIndexStats? vectorStats = null;
        if (vectorAvailable)
        {
            vectorStats = await searchService.GetStatsAsync();
        }

        return JsonSerializer.Serialize(new
        {
            version = typeof(DesignAidMcpTools).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown",
            assetCount = assets.Count,
            partCount = parts.Count,
            vectorSearch = new
            {
                available = vectorAvailable,
                pointCount = vectorStats?.PointCount ?? 0
            }
        }, JsonOptions);
    }

    // ========================================
    // プロジェクトコンテキスト（新規）
    // ========================================

    [McpServerTool, Description("プロジェクトの CLAUDE.md と DESIGN.md を解析し、開発に必要なコンテキスト（技術スタック、開発コマンド、概要）を構造化して返す。")]
    public static async Task<string> GetProjectContext(
        IAssetService assetService,
        [Description("装置名（プロジェクト名）")] string assetName)
    {
        var asset = await assetService.GetByNameAsync(assetName);
        if (asset == null)
            return JsonSerializer.Serialize(new { error = $"装置 '{assetName}' が見つかりません" }, JsonOptions);

        var assetPath = asset.DirectoryPath;
        var claudeMdPath = Path.Combine(assetPath, "CLAUDE.md");
        var designMdPath = Path.Combine(assetPath, "DESIGN.md");

        string? claudeContent = null;
        string? designContent = null;

        if (File.Exists(claudeMdPath))
            claudeContent = await File.ReadAllTextAsync(claudeMdPath);
        if (File.Exists(designMdPath))
            designContent = await File.ReadAllTextAsync(designMdPath);

        // DESIGN.md からセクションを抽出
        var techStack = ExtractSection(designContent, "技術スタック") ?? ExtractSection(designContent, "Tech Stack");
        var devCommands = ExtractSection(designContent, "開発コマンド") ?? ExtractSection(designContent, "Dev Commands");
        var overview = ExtractSection(designContent, "概要") ?? ExtractSection(designContent, "Overview");
        var dirStructure = ExtractSection(designContent, "ディレクトリ構造") ?? ExtractSection(designContent, "Directory Structure");

        // CLAUDE.md から言語ルールを抽出
        var languageRules = ExtractLanguageRules(claudeContent);

        // TL;DR（先頭の概要セクションまたは最初の200文字）
        var tldr = GenerateTldr(overview, techStack);

        return JsonSerializer.Serialize(new
        {
            name = asset.Name,
            displayName = asset.DisplayName,
            path = assetPath,
            hasClaude = claudeContent != null,
            hasDesign = designContent != null,
            tldr,
            language = languageRules,
            techStack,
            devCommands,
            overview,
            directoryStructure = dirStructure
        }, JsonOptions);
    }

    [McpServerTool, Description("プロジェクトの開発コマンド（ビルド、テスト、実行、フォーマット）を抽出して返す。")]
    public static async Task<string> GetDevCommands(
        IAssetService assetService,
        [Description("装置名（プロジェクト名）")] string assetName)
    {
        var asset = await assetService.GetByNameAsync(assetName);
        if (asset == null)
            return JsonSerializer.Serialize(new { error = $"装置 '{assetName}' が見つかりません" }, JsonOptions);

        var designMdPath = Path.Combine(asset.DirectoryPath, "DESIGN.md");
        if (!File.Exists(designMdPath))
            return JsonSerializer.Serialize(new { error = $"DESIGN.md が見つかりません: {designMdPath}" }, JsonOptions);

        var content = await File.ReadAllTextAsync(designMdPath);
        var commandsSection = ExtractSection(content, "開発コマンド") ?? ExtractSection(content, "Dev Commands") ?? "";

        // コードブロック内のコマンドを抽出
        var commands = ExtractCodeBlocks(commandsSection);

        return JsonSerializer.Serialize(new
        {
            assetName = asset.Name,
            path = asset.DirectoryPath,
            commandsRaw = commandsSection,
            codeBlocks = commands
        }, JsonOptions);
    }

    // ========================================
    // ヘルパーメソッド
    // ========================================

    /// <summary>
    /// Markdown からセクションを抽出する（## or ### レベル）。
    /// </summary>
    private static string? ExtractSection(string? content, string sectionName)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var lines = content.Split('\n');
        var capturing = false;
        var sectionLevel = 0;
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (capturing)
            {
                // 同レベル以上の見出しが来たら終了
                var headingLevel = GetHeadingLevel(line);
                if (headingLevel > 0 && headingLevel <= sectionLevel)
                    break;

                result.Add(line);
            }
            else if (line.TrimStart('#').Trim().StartsWith(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                capturing = true;
                sectionLevel = GetHeadingLevel(line);
            }
        }

        var text = string.Join('\n', result).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// CLAUDE.md から言語固有ルールのセクション名を検出する。
    /// </summary>
    private static string? ExtractLanguageRules(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("##") && line.Contains("Rules", StringComparison.OrdinalIgnoreCase))
            {
                var ruleName = line.TrimStart('#').Trim();
                // "Python Rules", "C# Rules" 等から言語名を抽出
                var language = ruleName.Replace("Rules", "").Replace("(MANDATORY)", "").Trim();
                if (!string.IsNullOrEmpty(language))
                    return language;
            }
        }

        return null;
    }

    /// <summary>
    /// TL;DR を生成する。
    /// </summary>
    private static string GenerateTldr(string? overview, string? techStack)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(overview))
        {
            // 最初の3行を取得
            var firstLines = overview.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(3);
            parts.AddRange(firstLines);
        }
        if (!string.IsNullOrEmpty(techStack))
        {
            // テーブルの最初の5行を取得
            var techLines = techStack.Split('\n')
                .Where(l => l.Contains('|') && !l.Contains("---"))
                .Take(5);
            parts.AddRange(techLines);
        }
        return string.Join('\n', parts);
    }

    /// <summary>
    /// Markdown のコードブロックを抽出する。
    /// </summary>
    private static List<string> ExtractCodeBlocks(string content)
    {
        var blocks = new List<string>();
        var lines = content.Split('\n');
        var inBlock = false;
        var currentBlock = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (inBlock)
                {
                    blocks.Add(string.Join('\n', currentBlock));
                    currentBlock.Clear();
                    inBlock = false;
                }
                else
                {
                    inBlock = true;
                }
            }
            else if (inBlock)
            {
                currentBlock.Add(line);
            }
        }

        return blocks;
    }

    /// <summary>
    /// 見出しレベルを取得する。
    /// </summary>
    private static int GetHeadingLevel(string line)
    {
        var trimmed = line.TrimStart();
        var level = 0;
        foreach (var c in trimmed)
        {
            if (c == '#') level++;
            else break;
        }
        return level > 0 && trimmed.Length > level && trimmed[level] == ' ' ? level : 0;
    }
}

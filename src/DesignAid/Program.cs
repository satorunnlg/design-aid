using System.Text.Json;
using DesignAid.Infrastructure.FileSystem;
using DesignAid.Domain.Entities;

namespace DesignAid;

/// <summary>
/// Design Aid CLI エントリーポイント
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "project" => await HandleProjectCommandAsync(args.Skip(1).ToArray()),
                "asset" => await HandleAssetCommandAsync(args.Skip(1).ToArray()),
                "component" => await HandleComponentCommandAsync(args.Skip(1).ToArray()),
                "--help" or "-h" => ShowHelp(),
                "--version" or "-v" => ShowVersion(),
                _ => ShowUnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("Design Aid - 機械設計支援システム");
        Console.WriteLine();
        Console.WriteLine("Usage: da <command> [subcommand] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  project    プロジェクト管理");
        Console.WriteLine("  asset      装置管理");
        Console.WriteLine("  component  コンポーネント管理");
        Console.WriteLine();
        Console.WriteLine("Use 'da <command> --help' for more information.");
        return 0;
    }

    private static int ShowVersion()
    {
        Console.WriteLine("Design Aid version 0.1.0");
        return 0;
    }

    private static int ShowUnknownCommand(string command)
    {
        Console.Error.WriteLine($"[ERROR] 不明なコマンド: {command}");
        Console.Error.WriteLine("'da --help' でコマンド一覧を表示");
        return 2;
    }

    private static async Task<int> HandleProjectCommandAsync(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("Usage: da project <subcommand> [options]");
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  add <path>    プロジェクトを登録");
            Console.WriteLine("  list          プロジェクト一覧");
            Console.WriteLine("  remove <path> プロジェクトを登録解除");
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "add" => await ProjectAddAsync(args.Skip(1).ToArray()),
            "list" => ProjectList(args.Skip(1).ToArray()),
            "remove" => ProjectRemove(args.Skip(1).ToArray()),
            _ => ShowUnknownCommand($"project {args[0]}")
        };
    }

    private static async Task<int> ProjectAddAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[ERROR] パスを指定してください");
            return 2;
        }

        var path = args[0];
        var name = GetOption(args, "--name");
        var create = HasFlag(args, "--create");

        var fullPath = Path.GetFullPath(path);

        if (create)
        {
            if (Directory.Exists(fullPath))
            {
                Console.Error.WriteLine($"[ERROR] ディレクトリは既に存在します: {fullPath}");
                return 1;
            }
            Directory.CreateDirectory(fullPath);
            Console.WriteLine($"ディレクトリを作成しました: {fullPath}");
        }
        else if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] ディレクトリが見つかりません: {fullPath}");
            return 1;
        }

        var projectMarkerService = new ProjectMarkerService();
        if (projectMarkerService.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] プロジェクトは既に登録されています: {fullPath}");
            return 1;
        }

        var projectName = name ?? Path.GetFileName(fullPath);
        var projectId = Guid.NewGuid();
        await projectMarkerService.CreateAsync(fullPath, projectId, projectName);

        var assetsDir = Path.Combine(fullPath, "assets");
        if (!Directory.Exists(assetsDir))
            Directory.CreateDirectory(assetsDir);

        Console.WriteLine();
        Console.WriteLine($"Project registered: {projectName}");
        Console.WriteLine($"  Path: {fullPath}");
        Console.WriteLine($"  ID: {projectId}");
        return 0;
    }

    private static int ProjectList(string[] args)
    {
        var searchPath = GetOption(args, "--path") ?? GetDefaultProjectsPath();
        var json = HasFlag(args, "--json");

        if (!Directory.Exists(searchPath))
        {
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(new { projects = Array.Empty<object>() }));
            else
                Console.WriteLine("登録済みプロジェクトはありません");
            return 0;
        }

        var projectMarkerService = new ProjectMarkerService();
        var assetJsonReader = new AssetJsonReader();
        var projects = new List<object>();

        foreach (var dir in Directory.GetDirectories(searchPath))
        {
            if (!projectMarkerService.Exists(dir)) continue;
            var marker = projectMarkerService.Read(dir);
            if (marker == null) continue;

            var assetsDir = Path.Combine(dir, "assets");
            var assetCount = Directory.Exists(assetsDir)
                ? Directory.GetDirectories(assetsDir).Count(d => assetJsonReader.Exists(d))
                : 0;

            projects.Add(new { name = marker.Name, path = dir, id = marker.ProjectId, assetCount });
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { projects }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (projects.Count == 0)
            {
                Console.WriteLine("登録済みプロジェクトはありません");
                return 0;
            }

            Console.WriteLine("Registered Projects:");
            Console.WriteLine();
            foreach (dynamic p in projects)
            {
                Console.WriteLine($"  {p.name}");
                Console.WriteLine($"    Path: {p.path}");
                Console.WriteLine($"    ID: {p.id}");
                Console.WriteLine($"    Assets: {p.assetCount}");
                Console.WriteLine();
            }
            Console.WriteLine($"Total: {projects.Count} project(s)");
        }
        return 0;
    }

    private static int ProjectRemove(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[ERROR] パスを指定してください");
            return 2;
        }

        var fullPath = Path.GetFullPath(args[0]);
        var delete = HasFlag(args, "--delete");
        var force = HasFlag(args, "--force");

        if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] ディレクトリが見つかりません: {fullPath}");
            return 1;
        }

        var projectMarkerService = new ProjectMarkerService();
        if (!projectMarkerService.Exists(fullPath))
        {
            Console.Error.WriteLine($"[ERROR] プロジェクトが登録されていません: {fullPath}");
            return 1;
        }

        var marker = projectMarkerService.Read(fullPath);
        var projectName = marker?.Name ?? Path.GetFileName(fullPath);

        if (!force)
        {
            Console.Write($"プロジェクト '{projectName}' を登録解除しますか？");
            if (delete) Console.Write(" [ディレクトリも削除されます]");
            Console.Write(" [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("キャンセルしました");
                return 0;
            }
        }

        projectMarkerService.Delete(fullPath);
        if (delete)
        {
            Directory.Delete(fullPath, recursive: true);
            Console.WriteLine($"プロジェクトを削除しました: {projectName}");
        }
        else
        {
            Console.WriteLine($"プロジェクトを登録解除しました: {projectName}");
        }
        return 0;
    }

    private static async Task<int> HandleAssetCommandAsync(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("Usage: da asset <subcommand> [options]");
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  add <name>     装置を追加");
            Console.WriteLine("  list           装置一覧");
            Console.WriteLine("  remove <name>  装置を削除");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --project <path>  プロジェクトパス（省略時はカレントディレクトリから検出）");
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "add" => await AssetAddAsync(args.Skip(1).ToArray()),
            "list" => AssetList(args.Skip(1).ToArray()),
            "remove" => AssetRemove(args.Skip(1).ToArray()),
            _ => ShowUnknownCommand($"asset {args[0]}")
        };
    }

    private static async Task<int> AssetAddAsync(string[] args)
    {
        var name = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("[ERROR] 装置名を指定してください");
            return 2;
        }

        var (projectPath, error) = ResolveProjectPath(args);
        if (projectPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        var assetPath = Path.Combine(projectPath, "assets", name);
        var assetJsonReader = new AssetJsonReader();
        if (Directory.Exists(assetPath) && assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置は既に存在します: {name}");
            return 1;
        }

        Directory.CreateDirectory(assetPath);
        var assetId = Guid.NewGuid();
        var displayName = GetOption(args, "--display-name") ?? name;
        await assetJsonReader.CreateAsync(assetPath, assetId, name, displayName, "");

        Console.WriteLine();
        Console.WriteLine($"Asset created: {name}");
        Console.WriteLine($"  Path: {assetPath}");
        Console.WriteLine($"  ID: {assetId}");
        return 0;
    }

    private static int AssetList(string[] args)
    {
        var (projectPath, error) = ResolveProjectPath(args);
        if (projectPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        var assetsDir = Path.Combine(projectPath, "assets");
        var assetJsonReader = new AssetJsonReader();
        var assets = new List<object>();

        if (Directory.Exists(assetsDir))
        {
            foreach (var dir in Directory.GetDirectories(assetsDir))
            {
                if (!assetJsonReader.Exists(dir)) continue;
                var assetJson = assetJsonReader.Read(dir);
                if (assetJson == null) continue;
                assets.Add(new { name = assetJson.Name, displayName = assetJson.DisplayName, id = assetJson.Id });
            }
        }

        var json = HasFlag(args, "--json");
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { assets }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (assets.Count == 0)
            {
                Console.WriteLine("装置はありません");
                return 0;
            }
            Console.WriteLine("Assets:");
            foreach (dynamic a in assets)
            {
                Console.WriteLine($"  {a.name} ({a.displayName})");
                Console.WriteLine($"    ID: {a.id}");
            }
            Console.WriteLine($"\nTotal: {assets.Count} asset(s)");
        }
        return 0;
    }

    private static int AssetRemove(string[] args)
    {
        var name = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("[ERROR] 装置名を指定してください");
            return 2;
        }

        var (projectPath, error) = ResolveProjectPath(args);
        if (projectPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        var assetPath = Path.Combine(projectPath, "assets", name);

        if (!Directory.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {name}");
            return 1;
        }

        var force = HasFlag(args, "--force");
        if (!force)
        {
            Console.Write($"装置 '{name}' を削除しますか？ [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("キャンセルしました");
                return 0;
            }
        }

        Directory.Delete(assetPath, recursive: true);
        Console.WriteLine($"装置を削除しました: {name}");
        return 0;
    }

    private static async Task<int> HandleComponentCommandAsync(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("Usage: da component <subcommand> [options]");
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  add <part-number> --name <name>  コンポーネントを追加");
            Console.WriteLine("  list                             コンポーネント一覧");
            Console.WriteLine("  remove <part-number>             コンポーネントを削除");
            Console.WriteLine("  link <part-number> --asset <name>  装置に紐付け");
            Console.WriteLine();
            Console.WriteLine("Options (link):");
            Console.WriteLine("  --project <path>  プロジェクトパス（省略時はカレントディレクトリから検出）");
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "add" => await ComponentAddAsync(args.Skip(1).ToArray()),
            "list" => ComponentList(args.Skip(1).ToArray()),
            "remove" => ComponentRemove(args.Skip(1).ToArray()),
            "link" => ComponentLink(args.Skip(1).ToArray()),
            _ => ShowUnknownCommand($"component {args[0]}")
        };
    }

    private static async Task<int> ComponentAddAsync(string[] args)
    {
        var partNumber = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
        var name = GetOption(args, "--name");
        var type = GetOption(args, "--type") ?? "Fabricated";

        if (string.IsNullOrEmpty(partNumber) || string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("[ERROR] 型式と --name オプションは必須です");
            return 2;
        }

        if (!Enum.TryParse<PartType>(type, ignoreCase: true, out var partType))
        {
            Console.Error.WriteLine($"[ERROR] 不明なパーツ種別: {type}");
            return 2;
        }

        var componentsDir = GetComponentsDirectory();
        Directory.CreateDirectory(componentsDir);
        var componentPath = Path.Combine(componentsDir, partNumber);

        var partJsonReader = new PartJsonReader();
        if (Directory.Exists(componentPath) && partJsonReader.Exists(componentPath))
        {
            Console.Error.WriteLine($"[ERROR] コンポーネントは既に存在します: {partNumber}");
            return 1;
        }

        Directory.CreateDirectory(componentPath);
        var partId = Guid.NewGuid();
        await partJsonReader.CreateAsync(componentPath, partId, partNumber, name, partType);

        Console.WriteLine();
        Console.WriteLine($"Component created: {partNumber}");
        Console.WriteLine($"  Name: {name}");
        Console.WriteLine($"  Type: {partType}");
        Console.WriteLine($"  Path: {componentPath}");
        Console.WriteLine($"  ID: {partId}");
        return 0;
    }

    private static int ComponentList(string[] args)
    {
        var componentsDir = GetComponentsDirectory();
        var partJsonReader = new PartJsonReader();
        var components = new List<object>();

        if (Directory.Exists(componentsDir))
        {
            foreach (var dir in Directory.GetDirectories(componentsDir))
            {
                if (!partJsonReader.Exists(dir)) continue;
                var partJson = partJsonReader.Read(dir);
                if (partJson == null) continue;
                var artifactCount = Directory.GetFiles(dir).Count(f => !Path.GetFileName(f).Equals("part.json", StringComparison.OrdinalIgnoreCase));
                components.Add(new { partNumber = partJson.PartNumber, name = partJson.Name, type = partJson.Type, id = partJson.Id, artifactCount });
            }
        }

        var json = HasFlag(args, "--json");
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { components }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (components.Count == 0)
            {
                Console.WriteLine("コンポーネントはありません");
                return 0;
            }
            Console.WriteLine("Shared Components:");
            foreach (dynamic c in components)
            {
                Console.WriteLine($"  {c.partNumber}");
                Console.WriteLine($"    Name: {c.name}");
                Console.WriteLine($"    Type: {c.type}");
                Console.WriteLine($"    Artifacts: {c.artifactCount} file(s)");
            }
            Console.WriteLine($"\nTotal: {components.Count} component(s)");
        }
        return 0;
    }

    private static int ComponentRemove(string[] args)
    {
        var partNumber = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
        if (string.IsNullOrEmpty(partNumber))
        {
            Console.Error.WriteLine("[ERROR] 型式を指定してください");
            return 2;
        }

        var componentPath = Path.Combine(GetComponentsDirectory(), partNumber);
        if (!Directory.Exists(componentPath))
        {
            Console.Error.WriteLine($"[ERROR] コンポーネントが見つかりません: {partNumber}");
            return 1;
        }

        var force = HasFlag(args, "--force");
        if (!force)
        {
            Console.Write($"コンポーネント '{partNumber}' を削除しますか？ [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("キャンセルしました");
                return 0;
            }
        }

        Directory.Delete(componentPath, recursive: true);
        Console.WriteLine($"コンポーネントを削除しました: {partNumber}");
        return 0;
    }

    private static int ComponentLink(string[] args)
    {
        var partNumber = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
        var asset = GetOption(args, "--asset");

        if (string.IsNullOrEmpty(partNumber) || string.IsNullOrEmpty(asset))
        {
            Console.Error.WriteLine("[ERROR] 型式と --asset オプションは必須です");
            return 2;
        }

        var (projectPath, error) = ResolveProjectPath(args);
        if (projectPath == null)
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        var assetPath = Path.Combine(projectPath, "assets", asset);
        var assetJsonReader = new AssetJsonReader();
        if (!assetJsonReader.Exists(assetPath))
        {
            Console.Error.WriteLine($"[ERROR] 装置が見つかりません: {asset}");
            return 1;
        }

        var componentPath = Path.Combine(GetComponentsDirectory(), partNumber);
        var partJsonReader = new PartJsonReader();
        if (!partJsonReader.Exists(componentPath))
        {
            Console.Error.WriteLine($"[ERROR] コンポーネントが見つかりません: {partNumber}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Component linked: {partNumber} -> {asset}");
        Console.WriteLine();
        Console.WriteLine("[INFO] 注意: 現在はファイルベースの管理のみです。");
        Console.WriteLine("       DB連携後、AssetComponents テーブルで紐付けが永続化されます。");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string GetDefaultProjectsPath()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return Path.Combine(dataDir, "projects");

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data", "projects");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data", "projects");
    }

    private static string GetComponentsDirectory()
    {
        var dataDir = Environment.GetEnvironmentVariable("DA_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir))
            return Path.Combine(dataDir, "components");

        var currentDir = Directory.GetCurrentDirectory();
        var dir = currentDir;
        while (dir != null)
        {
            if (IsRepositoryRoot(dir))
                return Path.Combine(dir, "data", "components");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(currentDir, "data", "components");
    }

    /// <summary>
    /// リポジトリルートかどうかを判定する。
    /// DesignAid.sln または DesignAid.slnx の存在で判定。
    /// </summary>
    private static bool IsRepositoryRoot(string dir)
    {
        return File.Exists(Path.Combine(dir, "DesignAid.sln"))
            || File.Exists(Path.Combine(dir, "DesignAid.slnx"));
    }

    /// <summary>
    /// カレントディレクトリから親方向に .da-project マーカーを検索し、
    /// プロジェクトディレクトリを返す。見つからない場合は null。
    /// </summary>
    private static string? FindProjectContext()
    {
        var projectMarkerService = new ProjectMarkerService();
        var dir = Directory.GetCurrentDirectory();

        while (dir != null)
        {
            if (projectMarkerService.Exists(dir))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// プロジェクトパスを解決する。
    /// --project オプションが指定されていればそれを使用、
    /// なければカレントディレクトリからプロジェクトコンテキストを検出。
    /// </summary>
    private static (string? path, string? error) ResolveProjectPath(string[] args)
    {
        var explicitProject = GetOption(args, "--project");

        if (!string.IsNullOrEmpty(explicitProject))
        {
            var fullPath = Path.GetFullPath(explicitProject);
            var projectMarkerService = new ProjectMarkerService();
            if (!projectMarkerService.Exists(fullPath))
                return (null, $"プロジェクトが見つかりません: {fullPath}");
            return (fullPath, null);
        }

        // 自動検出
        var detected = FindProjectContext();
        if (detected == null)
        {
            return (null, "プロジェクトディレクトリ内で実行するか、--project オプションを指定してください");
        }
        return (detected, null);
    }
}

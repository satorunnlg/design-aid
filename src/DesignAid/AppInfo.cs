using System.Reflection;

namespace DesignAid;

/// <summary>
/// アセンブリから取得するアプリ情報。
/// </summary>
/// <remarks>
/// <para>
/// <b>バージョンを文字列で書かない。</b> csproj の <c>&lt;Version&gt;</c> が唯一の正本で、
/// ここはそれをアセンブリ属性から読み出すだけである。
/// </para>
/// <para>
/// かつて同じ取得コードが <c>UpdateCommand</c> と <c>DesignAidMcpTools</c> に複製され、
/// <c>McpCommand</c> は**文字列でハードコード**していた。その結果、リリースのたびに
/// MCP サーバーが名乗るバージョンだけが取り残された。**採番元を 1 つにする。**
/// </para>
/// </remarks>
public static class AppInfo
{
    /// <summary>
    /// アプリのバージョン（例: <c>0.6.0-alpha</c>）。
    /// </summary>
    /// <remarks>
    /// <c>InformationalVersion</c> を使う。csproj で
    /// <c>IncludeSourceRevisionInInformationalVersion=false</c> を指定しているため
    /// 通常はコミットハッシュが付かないが、**付いた場合に備えて <c>+</c> 以降を落とす**。
    /// </remarks>
    public static string Version { get; } = Resolve();

    private static string Resolve()
    {
        var version = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        // +metadata（ソースリビジョン等）を除去する
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}

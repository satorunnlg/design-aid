using DesignAid;

namespace DesignAid.Tests.Domain;

/// <summary>
/// AppInfo のテスト。
/// </summary>
/// <remarks>
/// バージョンは csproj の <c>&lt;Version&gt;</c> が正本なので、**具体的な値を固定しない**
/// （固定すると版上げのたびにテストを直すことになり、正本が 2 つになる）。
/// 検査するのは「取得できていること」と「表記が壊れていないこと」だけである。
/// </remarks>
public class AppInfoTests
{
    [Fact]
    public void Version_取得できる()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }

    [Fact]
    public void Version_取得失敗の既定値になっていない()
    {
        // アセンブリ属性が読めないと "0.0.0" に落ちる。落ちていたら配線が壊れている。
        Assert.NotEqual("0.0.0", AppInfo.Version);
    }

    [Fact]
    public void Version_ソースリビジョンが混ざらない()
    {
        // MCP の ServerInfo やリリース比較に使うため、"+<hash>" が付いていると困る。
        Assert.DoesNotContain("+", AppInfo.Version);
    }

    [Fact]
    public void Version_何度読んでも同じ値()
    {
        Assert.Equal(AppInfo.Version, AppInfo.Version);
    }
}

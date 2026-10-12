using DesignAid.Mcp;

namespace DesignAid.Tests.Application;

/// <summary>
/// GetProjectContext が読む指示ファイルの選択（AGENTS.md 優先）のテスト。
/// </summary>
public class InstructionsFileSelectionTests
{
    [Theory]
    [InlineData(true, false, "AGENTS.md")]
    [InlineData(false, true, "CLAUDE.md")]
    [InlineData(true, true, "AGENTS.md")]
    [InlineData(false, false, null)]
    public void SelectInstructionsFile_は_AGENTS優先で選ぶ(bool agents, bool claude, string? expected)
    {
        Assert.Equal(expected, DesignAidMcpTools.SelectInstructionsFile(agents, claude));
    }
}

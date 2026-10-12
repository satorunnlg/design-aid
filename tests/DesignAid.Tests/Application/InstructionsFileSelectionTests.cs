using System.Text.Json;
using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Mcp;
using Moq;

namespace DesignAid.Tests.Application;

/// <summary>
/// GetProjectContext が読む指示ファイルの選択（AGENTS.md 優先）のテスト。
/// </summary>
public class InstructionsFileSelectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "daid-instr-" + Guid.NewGuid().ToString("N"));

    public InstructionsFileSelectionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true, false, "AGENTS.md")]
    [InlineData(false, true, "CLAUDE.md")]
    [InlineData(true, true, "AGENTS.md")]
    [InlineData(false, false, null)]
    public void SelectInstructionsFile_は_AGENTS優先で選ぶ(bool agents, bool claude, string? expected)
    {
        Assert.Equal(expected, DesignAidMcpTools.SelectInstructionsFile(agents, claude));
    }

    private async Task<JsonElement> GetContextAsync()
    {
        var asset = Asset.Create("ctx-asset", _dir);
        var mock = new Mock<IAssetService>();
        mock.Setup(x => x.GetByNameAsync("ctx-asset")).ReturnsAsync(asset);
        var json = await DesignAidMcpTools.GetProjectContext(mock.Object, "ctx-asset");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task GetProjectContext_は両方ある場合AGENTS_mdの内容を読む()
    {
        File.WriteAllText(Path.Combine(_dir, "AGENTS.md"), "## Agentslang Rules\n");
        File.WriteAllText(Path.Combine(_dir, "CLAUDE.md"), "## Claudelang Rules\n");

        var root = await GetContextAsync();

        Assert.Equal("AGENTS.md", root.GetProperty("instructionsFile").GetString());
        Assert.Equal("Agentslang", root.GetProperty("language").GetString());
    }

    [Fact]
    public async Task GetProjectContext_はCLAUDE_mdのみの場合その内容を読む()
    {
        File.WriteAllText(Path.Combine(_dir, "CLAUDE.md"), "## Claudelang Rules\n");

        var root = await GetContextAsync();

        Assert.Equal("CLAUDE.md", root.GetProperty("instructionsFile").GetString());
        Assert.Equal("Claudelang", root.GetProperty("language").GetString());
    }
}

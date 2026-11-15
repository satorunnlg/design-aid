using DesignAid.Application.Services;

namespace DesignAid.Tests.Application;

public class SafeZipTests
{
    private static readonly string Dest = Path.Combine(Path.GetTempPath(), "design-aid-safezip", "dest");

    [Theory]
    [InlineData("assets/a/asset.json")]
    [InlineData("a.txt")]
    [InlineData("sub/../ok.txt")]
    [InlineData("assets/")]
    public void 展開先の中に収まるエントリは_通る(string name)
    {
        Assert.Null(SafeZip.FindEscapingEntry(Dest, new[] { name }));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("..")]
    [InlineData("../dest-sibling/x")]
    public void 展開先の外を指すエントリは_検出する(string name)
    {
        Assert.Equal(name, SafeZip.FindEscapingEntry(Dest, new[] { "ok.txt", name }));
    }

    [Fact]
    public void 絶対パスのエントリは_検出する()
    {
        var abs = Path.Combine(Path.GetTempPath(), "design-aid-safezip", "elsewhere", "x.txt");
        Assert.Equal(abs, SafeZip.FindEscapingEntry(Dest, new[] { abs }));
    }

    [Fact]
    public void 展開先と前方一致するだけの別のディレクトリは_外とみなす()
    {
        Assert.NotNull(SafeZip.FindEscapingEntry(Dest, new[] { "../dest2/x.txt" }));
    }

    [Fact]
    public void ZipFile_ExtractToDirectory自体も_外を指すエントリを拒否する_archive_restoreとupdateの前提()
    {
        // archive restore と update は ZipFile.ExtractToDirectory を使う。.NET がこの拒否を保証していることを固定する
        var tmp = Path.Combine(Path.GetTempPath(), "design-aid-safezip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var zipPath = Path.Combine(tmp, "evil.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(zip.CreateEntry("../escaped.txt").Open());
                w.Write("x");
            }
            var dest = Path.Combine(tmp, "dest");
            Assert.ThrowsAny<IOException>(() => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dest));
            Assert.False(File.Exists(Path.Combine(tmp, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}

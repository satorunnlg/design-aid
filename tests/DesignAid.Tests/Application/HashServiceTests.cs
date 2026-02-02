using DesignAid.Application.Services;
using DesignAid.Domain.Entities;
using DesignAid.Domain.Exceptions;
using DesignAid.Domain.ValueObjects;

namespace DesignAid.Tests.Application;

/// <summary>
/// HashService のテスト。
/// </summary>
public class HashServiceTests : IDisposable
{
    private readonly HashService _service;
    private readonly string _tempDir;

    public HashServiceTests()
    {
        _service = new HashService();
        _tempDir = Path.Combine(Path.GetTempPath(), $"HashServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ComputeHash_ValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "test content");

        // Act
        var hash = _service.ComputeHash(filePath);

        // Assert
        Assert.StartsWith("sha256:", hash.Value);
        Assert.Equal(71, hash.Value.Length); // sha256: + 64 hex chars
    }

    [Fact]
    public void ComputeHash_SameContent_ReturnsSameHash()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "test1.txt");
        var file2 = Path.Combine(_tempDir, "test2.txt");
        File.WriteAllText(file1, "identical content");
        File.WriteAllText(file2, "identical content");

        // Act
        var hash1 = _service.ComputeHash(file1);
        var hash2 = _service.ComputeHash(file2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentContent_ReturnsDifferentHash()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "test1.txt");
        var file2 = Path.Combine(_tempDir, "test2.txt");
        File.WriteAllText(file1, "content A");
        File.WriteAllText(file2, "content B");

        // Act
        var hash1 = _service.ComputeHash(file1);
        var hash2 = _service.ComputeHash(file2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_FileNotFound_ThrowsIntegrityException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.txt");

        // Act & Assert
        var ex = Assert.Throws<IntegrityException>(() => _service.ComputeHash(nonExistentPath));
        Assert.Contains("見つかりません", ex.Message);
    }

    [Fact]
    public void ComputeHash_EmptyFile_ReturnsKnownHash()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "empty.txt");
        File.WriteAllText(filePath, "");

        // Act
        var hash = _service.ComputeHash(filePath);

        // Assert
        // 空ファイルの SHA256 ハッシュは既知の値
        Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash.Value);
    }

    [Fact]
    public void VerifyHash_MatchingHash_ReturnsTrue()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "test content");
        var expectedHash = _service.ComputeHash(filePath);

        // Act
        var result = _service.VerifyHash(filePath, expectedHash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyHash_MismatchingHash_ReturnsFalse()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "test content");
        var wrongHash = new FileHash("sha256:0000000000000000000000000000000000000000000000000000000000000000");

        // Act
        var result = _service.VerifyHash(filePath, wrongHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyHash_FileNotFound_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.txt");
        var hash = new FileHash("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act
        var result = _service.VerifyHash(nonExistentPath, hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CombineHashes_EmptyCollection_ReturnsEmptyHash()
    {
        // Act
        var combined = _service.CombineHashes(Array.Empty<FileHash>());

        // Assert
        Assert.StartsWith("sha256:", combined.Value);
    }

    [Fact]
    public void CombineHashes_SameHashes_SameOrder_ReturnsSameResult()
    {
        // Arrange
        var hash1 = new FileHash("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var hash2 = new FileHash("sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        // Act
        var combined1 = _service.CombineHashes(new[] { hash1, hash2 });
        var combined2 = _service.CombineHashes(new[] { hash2, hash1 }); // 逆順

        // Assert
        // ソートされるため同じ結果になるはず
        Assert.Equal(combined1, combined2);
    }

    [Fact]
    public void ComputeDirectoryHashes_ReturnsHashesForAllFiles()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_tempDir, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(subDir, "file2.txt"), "content2");

        // Act
        var hashes = _service.ComputeDirectoryHashes(_tempDir);

        // Assert
        Assert.Equal(2, hashes.Count);
        Assert.Contains("file1.txt", hashes.Keys);
        Assert.Contains(Path.Combine("sub", "file2.txt"), hashes.Keys);
    }

    [Fact]
    public void ComputeDirectoryHashes_ExcludesPartJson()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "file.txt"), "content");
        File.WriteAllText(Path.Combine(_tempDir, "part.json"), "{}");

        // Act
        var hashes = _service.ComputeDirectoryHashes(_tempDir);

        // Assert
        Assert.Single(hashes);
        Assert.Contains("file.txt", hashes.Keys);
        Assert.DoesNotContain("part.json", hashes.Keys);
    }

    [Fact]
    public async Task ComputeHashAsync_ValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        // Act
        var hash = await _service.ComputeHashAsync(filePath);

        // Assert
        Assert.StartsWith("sha256:", hash.Value);

        // 同期版と同じ結果
        var syncHash = _service.ComputeHash(filePath);
        Assert.Equal(syncHash, hash);
    }

    [Fact]
    public void ValidateComponentIntegrity_AllFilesMatch_ReturnsOk()
    {
        // Arrange
        var componentDir = Path.Combine(_tempDir, "component");
        Directory.CreateDirectory(componentDir);
        File.WriteAllText(Path.Combine(componentDir, "drawing.dxf"), "drawing content");

        var partNumber = new PartNumber("TEST-001");
        var part = FabricatedPart.Create(partNumber, "テスト部品", componentDir);

        // ハッシュを設定
        var hash = _service.ComputeHash(Path.Combine(componentDir, "drawing.dxf"));
        part.UpdateArtifactHash("drawing.dxf", hash);

        // Act
        var result = _service.ValidateComponentIntegrity(part);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateComponentIntegrity_FileMissing_ReturnsError()
    {
        // Arrange
        var componentDir = Path.Combine(_tempDir, "component2");
        Directory.CreateDirectory(componentDir);

        var partNumber = new PartNumber("TEST-002");
        var part = FabricatedPart.Create(partNumber, "テスト部品", componentDir);

        // 存在しないファイルのハッシュを設定
        var fakeHash = new FileHash("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        part.UpdateArtifactHash("missing.dxf", fakeHash);

        // Act
        var result = _service.ValidateComponentIntegrity(part);

        // Assert
        Assert.True(result.HasErrors);
        Assert.Contains("見つかりません", result.Details[0].Message);
    }

    [Fact]
    public void ValidateComponentIntegrity_HashMismatch_ReturnsWarning()
    {
        // Arrange
        var componentDir = Path.Combine(_tempDir, "component3");
        Directory.CreateDirectory(componentDir);
        File.WriteAllText(Path.Combine(componentDir, "drawing.dxf"), "original content");

        var partNumber = new PartNumber("TEST-003");
        var part = FabricatedPart.Create(partNumber, "テスト部品", componentDir);

        // 古いハッシュを設定
        var oldHash = new FileHash("sha256:0000000000000000000000000000000000000000000000000000000000000000");
        part.UpdateArtifactHash("drawing.dxf", oldHash);

        // ファイルを変更
        File.WriteAllText(Path.Combine(componentDir, "drawing.dxf"), "modified content");

        // Act
        var result = _service.ValidateComponentIntegrity(part);

        // Assert
        Assert.True(result.HasWarnings);
        Assert.Contains("ハッシュ不整合", result.Details[0].Message);
    }
}

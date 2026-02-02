using DesignAid.Domain.ValueObjects;

namespace DesignAid.Tests.Domain;

/// <summary>
/// FileHash 値オブジェクトのテスト。
/// </summary>
public class FileHashTests
{
    private const string ValidHash = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string ValidHexOnly = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void Constructor_ValidHash_CreatesInstance()
    {
        // Act
        var hash = new FileHash(ValidHash);

        // Assert
        Assert.Equal(ValidHash, hash.Value);
    }

    [Fact]
    public void Constructor_UppercaseHash_NormalizesToLowercase()
    {
        // Arrange
        var uppercase = "sha256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

        // Act
        var hash = new FileHash(uppercase);

        // Assert
        Assert.Equal(ValidHash, hash.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrEmpty_ThrowsArgumentException(string? value)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new FileHash(value!));
        Assert.Contains("ハッシュ値は必須です", ex.Message);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("sha256:tooshort")]
    [InlineData("md5:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 非16進数
    public void Constructor_InvalidFormat_ThrowsArgumentException(string value)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new FileHash(value));
        Assert.Contains("sha256: プレフィックス", ex.Message);
    }

    [Fact]
    public void FromHex_WithoutPrefix_AddsPrefix()
    {
        // Act
        var hash = FileHash.FromHex(ValidHexOnly);

        // Assert
        Assert.Equal(ValidHash, hash.Value);
    }

    [Fact]
    public void FromHex_WithPrefix_CreatesNormally()
    {
        // Act
        var hash = FileHash.FromHex(ValidHash);

        // Assert
        Assert.Equal(ValidHash, hash.Value);
    }

    [Fact]
    public void FromBytes_ValidBytes_CreatesHash()
    {
        // Arrange
        var bytes = new byte[32]; // 全てゼロのハッシュ

        // Act
        var hash = FileHash.FromBytes(bytes);

        // Assert
        Assert.StartsWith("sha256:", hash.Value);
        Assert.Equal(71, hash.Value.Length); // sha256: (7) + 64 hex chars
    }

    [Fact]
    public void FromBytes_InvalidLength_ThrowsArgumentException()
    {
        // Arrange
        var bytes = new byte[16]; // 短すぎる

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => FileHash.FromBytes(bytes));
        Assert.Contains("32バイト", ex.Message);
    }

    [Fact]
    public void ToHexString_ReturnsWithoutPrefix()
    {
        // Arrange
        var hash = new FileHash(ValidHash);

        // Act
        var hex = hash.ToHexString();

        // Assert
        Assert.Equal(ValidHexOnly, hex);
        Assert.DoesNotContain("sha256:", hex);
    }

    [Theory]
    [InlineData("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    public void IsValid_ReturnsExpectedResult(string? value, bool expected)
    {
        // Act
        var result = FileHash.IsValid(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryCreate_ValidValue_ReturnsTrue()
    {
        // Act
        var success = FileHash.TryCreate(ValidHash, out var result);

        // Assert
        Assert.True(success);
        Assert.Equal(ValidHash, result.Value);
    }

    [Fact]
    public void TryCreate_InvalidValue_ReturnsFalse()
    {
        // Act
        var success = FileHash.TryCreate("invalid", out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void Equality_SameHash_AreEqual()
    {
        // Arrange
        var h1 = new FileHash(ValidHash);
        var h2 = new FileHash(ValidHash);

        // Assert
        Assert.Equal(h1, h2);
        Assert.True(h1 == h2);
    }

    [Fact]
    public void Equality_DifferentCase_AreEqual()
    {
        // Arrange
        var h1 = new FileHash(ValidHash);
        var h2 = new FileHash(ValidHash.ToUpperInvariant());

        // Assert
        Assert.Equal(h1, h2);
    }
}

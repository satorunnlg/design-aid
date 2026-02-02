using DesignAid.Domain.ValueObjects;

namespace DesignAid.Tests.Domain;

/// <summary>
/// PartNumber 値オブジェクトのテスト。
/// </summary>
public class PartNumberTests
{
    [Theory]
    [InlineData("SP-2026-PLATE-01")]
    [InlineData("MTR-001")]
    [InlineData("ABC_123")]
    [InlineData("part.v1")]
    [InlineData("A1")]
    public void Constructor_ValidPartNumber_CreatesInstance(string value)
    {
        // Act
        var partNumber = new PartNumber(value);

        // Assert
        Assert.Equal(value, partNumber.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrEmpty_ThrowsArgumentException(string? value)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PartNumber(value!));
        Assert.Contains("型式は必須です", ex.Message);
    }

    [Theory]
    [InlineData("SP 2026")]      // スペース含む
    [InlineData("SP/2026")]      // スラッシュ含む
    [InlineData("SP@2026")]      // 特殊文字含む
    [InlineData("日本語パーツ")]  // 日本語
    public void Constructor_InvalidCharacters_ThrowsArgumentException(string value)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new PartNumber(value));
        Assert.Contains("英数字、ハイフン、アンダースコア、ピリオドのみ", ex.Message);
    }

    [Theory]
    [InlineData("SP-2026-PLATE-01", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("invalid/char", false)]
    public void IsValid_ReturnsExpectedResult(string? value, bool expected)
    {
        // Act
        var result = PartNumber.IsValid(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryCreate_ValidValue_ReturnsTrue()
    {
        // Act
        var success = PartNumber.TryCreate("SP-001", out var result);

        // Assert
        Assert.True(success);
        Assert.Equal("SP-001", result.Value);
    }

    [Fact]
    public void TryCreate_InvalidValue_ReturnsFalse()
    {
        // Act
        var success = PartNumber.TryCreate("invalid/char", out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void ImplicitConversion_ToString_ReturnsValue()
    {
        // Arrange
        var partNumber = new PartNumber("SP-001");

        // Act
        string stringValue = partNumber;

        // Assert
        Assert.Equal("SP-001", stringValue);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var pn1 = new PartNumber("SP-001");
        var pn2 = new PartNumber("SP-001");

        // Assert
        Assert.Equal(pn1, pn2);
        Assert.True(pn1 == pn2);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        // Arrange
        var pn1 = new PartNumber("SP-001");
        var pn2 = new PartNumber("SP-002");

        // Assert
        Assert.NotEqual(pn1, pn2);
        Assert.True(pn1 != pn2);
    }
}

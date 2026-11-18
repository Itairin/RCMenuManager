using RCMenuManager.Helpers;
using Xunit;

namespace RCMenuManager.Tests;

public class MenuTextHelperTests
{
    [Theory]
    [InlineData("显示设置(&D)", "显示设置(D)")]
    [InlineData("查看(&V)", "查看(V)")]
    [InlineData("Open Git &GUI here", "Open Git GUI here")]
    [InlineData("通过 Code 打开", "通过 Code 打开")]
    [InlineData("压缩(zipped)文件夹", "压缩(zipped)文件夹")]
    public void StripAccessKeys_removes_the_marker_but_keeps_the_letter(string input, string expected)
        => Assert.Equal(expected, MenuTextHelper.StripAccessKeys(input));

    [Fact]
    public void StripAccessKeys_keeps_a_literal_ampersand()
        => Assert.Equal("A & B", MenuTextHelper.StripAccessKeys("A && B"));

    [Fact]
    public void StripAccessKeys_tolerates_null_and_empty()
    {
        Assert.Equal(string.Empty, MenuTextHelper.StripAccessKeys(null));
        Assert.Equal(string.Empty, MenuTextHelper.StripAccessKeys(string.Empty));
    }

    [Fact]
    public void StripAccessKeys_drops_a_trailing_marker()
        => Assert.Equal("名称", MenuTextHelper.StripAccessKeys("名称&"));
}

using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class WinVersionServiceTests
{
    [Theory]
    [InlineData(22000, true)]
    [InlineData(22621, true)]
    [InlineData(19045, false)]
    [InlineData(10240, false)]
    public void IsWindows11_reflects_build_threshold(int build, bool expected)
    {
        var svc = new FakeWinVersionService(build);
        Assert.Equal(expected, svc.IsWindows11);
    }

    private sealed class FakeWinVersionService : WinVersionService
    {
        private readonly int _build;
        public FakeWinVersionService(int build) { _build = build; }
        public override int Build => _build;
    }
}
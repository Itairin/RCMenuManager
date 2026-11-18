using System;
using System.IO;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class SettingsServiceTests
{
    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Save_then_reload_keeps_every_field()
    {
        var path = TempPath();
        var first = new SettingsService(path);
        first.Current.WindowWidth = 1440;
        first.Current.WindowHeight = 900;
        first.Current.WindowLeft = 120;
        first.Current.WindowTop = 80;
        first.Current.IsMaximized = true;
        first.Current.ListColumnRatio = 0.72;
        first.Current.LastScopeId = "Folder";
        first.Current.ShowExtended = true;
        first.Save();

        var second = new SettingsService(path);

        Assert.Equal(1440, second.Current.WindowWidth, 3);
        Assert.Equal(900, second.Current.WindowHeight, 3);
        Assert.Equal(120, second.Current.WindowLeft!.Value, 3);
        Assert.Equal(80, second.Current.WindowTop!.Value, 3);
        Assert.True(second.Current.IsMaximized);
        Assert.Equal(0.72, second.Current.ListColumnRatio, 3);
        Assert.Equal("Folder", second.Current.LastScopeId);
        Assert.True(second.Current.ShowExtended);
    }

    [Fact]
    public void Missing_file_falls_back_to_defaults()
    {
        var svc = new SettingsService(TempPath());

        Assert.Equal(1180, svc.Current.WindowWidth, 3);
        Assert.Equal(720, svc.Current.WindowHeight, 3);
        Assert.Null(svc.Current.LastScopeId);
        Assert.False(svc.Current.ShowExtended);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        var svc = new SettingsService(path);

        Assert.Equal(1180, svc.Current.WindowWidth, 3);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"windowWidth\":10,\"windowHeight\":999999,\"listColumnRatio\":0.99}");

        var svc = new SettingsService(path);

        Assert.Equal(1180, svc.Current.WindowWidth, 3);
        Assert.Equal(720, svc.Current.WindowHeight, 3);
        Assert.Equal(0.606, svc.Current.ListColumnRatio, 3);
    }

    [Fact]
    public void Save_is_silent_when_the_target_directory_is_unusable()
    {
        // A path under a file (not a directory) cannot be created; saving must not throw.
        var file = Path.Combine(Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        var svc = new SettingsService(Path.Combine(file, "settings.json"));

        svc.Current.LastScopeId = "Folder";
        svc.Save();

        Assert.Equal("Folder", svc.Current.LastScopeId);
    }
}

using System;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// HKCU smoke test that touches the real registry, writes / reads / deletes
/// under a unique sandbox sub-key and tears it down afterwards.
/// </summary>
[Collection("RealRegistry")]
public class Win32RegistryWriterTests : IDisposable
{
    private readonly string _sandbox = $@"Software\RCMenuManager.Tests\{Guid.NewGuid():N}";
    private readonly Win32RegistryWriter _writer = new();

    [Fact]
    public void Round_trip_create_set_delete_value()
    {
        _writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox);
        Assert.True(_writer.KeyExists(RegistryHive.CurrentUser, _sandbox));

        _writer.SetStringValue(RegistryHive.CurrentUser, _sandbox, "Name", "Hello");
        Assert.True(_writer.ValueExists(RegistryHive.CurrentUser, _sandbox, "Name"));

        _writer.DeleteValue(RegistryHive.CurrentUser, _sandbox, "Name");
        Assert.False(_writer.ValueExists(RegistryHive.CurrentUser, _sandbox, "Name"));
    }

    [Fact]
    public void DeleteSubKeyTree_removes_nested()
    {
        _writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox + @"\Child");
        Assert.True(_writer.KeyExists(RegistryHive.CurrentUser, _sandbox + @"\Child"));
        _writer.DeleteSubKeyTree(RegistryHive.CurrentUser, _sandbox);
        Assert.False(_writer.KeyExists(RegistryHive.CurrentUser, _sandbox));
    }

    public void Dispose()
    {
        try { _writer.DeleteSubKeyTree(RegistryHive.CurrentUser, _sandbox); }
        catch { /* best effort cleanup */ }
    }
}

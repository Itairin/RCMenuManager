using Microsoft.Win32;
using Xunit;

namespace RCMenuManager.Tests;

public class InMemoryRegistryWriterTests
{
    [Fact]
    public void CreateSubKey_then_KeyExists_is_true()
    {
        var writer = new InMemoryRegistryWriter();
        Assert.False(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Foo"));
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Foo");
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Foo"));
    }

    [Fact]
    public void SetStringValue_round_trips_via_ValueExists()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Foo");
        writer.SetStringValue(RegistryHive.CurrentUser, @"Software\Foo", "Name", "Bar");
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, @"Software\Foo", "Name"));
    }

    [Fact]
    public void DeleteValue_removes_value_but_keeps_key()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Foo");
        writer.SetStringValue(RegistryHive.CurrentUser, @"Software\Foo", "Name", "Bar");
        writer.DeleteValue(RegistryHive.CurrentUser, @"Software\Foo", "Name");
        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, @"Software\Foo", "Name"));
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Foo"));
    }

    [Fact]
    public void DeleteSubKeyTree_removes_descendants()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Foo\Bar");
        writer.DeleteSubKeyTree(RegistryHive.CurrentUser, @"Software\Foo");
        Assert.False(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Foo"));
        Assert.False(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Foo\Bar"));
    }

    [Fact]
    public void Hives_are_isolated()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Foo");
        Assert.False(writer.KeyExists(RegistryHive.LocalMachine, @"Software\Foo"));
    }
}

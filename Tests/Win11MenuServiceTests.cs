using System;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

[Collection("RealRegistry")]
public class Win11MenuServiceTests : IDisposable
{
    private readonly string _toggleRoot;
    private readonly string _blockRoot;
    private readonly Win11MenuService _svc;

    public Win11MenuServiceTests()
    {
        var suffix = Guid.NewGuid().ToString("N");
        _toggleRoot = $@"Software\RCMenuManager.Tests\Win11Toggle\{suffix}";
        _blockRoot = $@"Software\RCMenuManager.Tests\Win11Block\{suffix}";
        _svc = new Win11MenuService(_toggleRoot, _blockRoot);
        Cleanup();
    }

    private void Cleanup()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_toggleRoot, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(_blockRoot, throwOnMissingSubKey: false); } catch { }
    }

    [Fact]
    public void IsNewMenuEnabled_defaults_to_true_when_key_absent()
    {
        Assert.True(_svc.IsNewMenuEnabled);
    }

    [Fact]
    public void SetNewMenuEnabled_false_creates_inprocserver32_with_empty_default()
    {
        _svc.SetNewMenuEnabled(false);
        Assert.False(_svc.IsNewMenuEnabled);
        using var k = Registry.CurrentUser.OpenSubKey(_toggleRoot + @"\InprocServer32");
        Assert.NotNull(k);
        Assert.Equal("", k.GetValue(""));
    }

    [Fact]
    public void SetNewMenuEnabled_true_removes_toggle_key()
    {
        _svc.SetNewMenuEnabled(false);
        Assert.False(_svc.IsNewMenuEnabled);
        _svc.SetNewMenuEnabled(true);
        Assert.True(_svc.IsNewMenuEnabled);
        using var k = Registry.CurrentUser.OpenSubKey(_toggleRoot, writable: false);
        Assert.Null(k);
    }

    [Fact]
    public void SetNewMenuEnabled_is_idempotent()
    {
        _svc.SetNewMenuEnabled(true);
        Assert.True(_svc.IsNewMenuEnabled);
        _svc.SetNewMenuEnabled(true);
        Assert.True(_svc.IsNewMenuEnabled);
    }

    [Fact]
    public void GetBlockList_returns_empty_when_root_absent()
    {
        Assert.Empty(_svc.GetBlockList());
    }

    [Fact]
    public void GetBlockList_returns_subkey_names()
    {
        Registry.CurrentUser.CreateSubKey(_blockRoot + @"\Verb1");
        Registry.CurrentUser.CreateSubKey(_blockRoot + @"\Verb2");
        Registry.CurrentUser.CreateSubKey(_blockRoot + @"\Verb3");

        var list = _svc.GetBlockList();
        Assert.Equal(3, list.Count);
        Assert.Contains(list, b => b.VerbName == "Verb1");
        Assert.Contains(list, b => b.VerbName == "Verb2");
        Assert.Contains(list, b => b.VerbName == "Verb3");
    }

    [Fact]
    public void RemoveFromBlock_deletes_existing_subkey()
    {
        Registry.CurrentUser.CreateSubKey(_blockRoot + @"\ToDelete");
        Assert.Single(_svc.GetBlockList());

        _svc.RemoveFromBlock("ToDelete");

        Assert.Empty(_svc.GetBlockList());
    }

    [Fact]
    public void RemoveFromBlock_does_not_throw_for_missing_verb()
    {
        _svc.RemoveFromBlock("NeverExisted");
        Assert.Empty(_svc.GetBlockList());
    }

    [Fact]
    public void RemoveFromBlock_ignores_empty_input()
    {
        _svc.RemoveFromBlock("");
        _svc.RemoveFromBlock("   ");
        _svc.RemoveFromBlock(null!);
    }

    public void Dispose() => Cleanup();
}
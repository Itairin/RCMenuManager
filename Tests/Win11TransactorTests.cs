using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// The Win11 toggle used to write to the registry directly: no backup, no audit line.
/// These checks pin the funnel in place — every mutation must reach the transactor
/// exactly once, with the key it claims to change.
/// </summary>
public class Win11TransactorTests
{
    private sealed class RecordingTransactor : IRegistryTransactor
    {
        public List<(RegistryHive Hive, string SubKey, string ScopeId, string VerbName, string Op)> Calls { get; } = new();

        public bool CanWrite(RegistryHive hive) => true;

        public void Run(RegistryHive hive, string subKey, string scopeId, string verbName, string op,
            Action<IRegistryWriter> mutate)
        {
            Calls.Add((hive, subKey, scopeId, verbName, op));
            // Deliberately not calling mutate: these tests assert the routing, and the
            // real writer would touch the live CLSID key.
        }
    }

    private sealed class FakeWriter : IRegistryWriter
    {
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool KeyExists(RegistryHive hive, string subKey) => Keys.Contains(subKey);
        public void CreateSubKey(RegistryHive hive, string subKey) => Keys.Add(subKey);
        public void DeleteSubKeyTree(RegistryHive hive, string subKey) => Keys.Remove(subKey);
        public void SetStringValue(RegistryHive hive, string subKey, string name, string value) => Keys.Add(subKey);
        public void DeleteValue(RegistryHive hive, string subKey, string name) { }
        public bool ValueExists(RegistryHive hive, string subKey, string name) => false;
    }

    [Fact]
    public void Toggle_writes_go_through_the_transactor_with_a_win11_scope_id()
    {
        var sink = new RecordingTransactor();
        var svc = new Win11MenuService(@"Software\RCMenuManager.Tests\T\" + Guid.NewGuid().ToString("N"),
                                        @"Software\RCMenuManager.Tests\B\" + Guid.NewGuid().ToString("N"),
                                        sink);

        svc.SetNewMenuEnabled(false);

        var call = Assert.Single(sink.Calls);
        Assert.Equal(RegistryHive.CurrentUser, call.Hive);
        Assert.Equal("Win11", call.ScopeId);
        Assert.Equal("DisableWin11Menu", call.Op);
        Assert.Contains("InprocServer32", call.SubKey);
    }

    [Fact]
    public void Removing_a_block_entry_goes_through_the_transactor()
    {
        var blockRoot = @"Software\RCMenuManager.Tests\B\" + Guid.NewGuid().ToString("N");
        var sink = new RecordingTransactor();
        var svc = new Win11MenuService(@"Software\RCMenuManager.Tests\T\x", blockRoot, sink);

        svc.RemoveFromBlock("SomeVerb");

        var call = Assert.Single(sink.Calls);
        Assert.Equal("RemoveBlock", call.Op);
        Assert.Equal("SomeVerb", call.VerbName);
        Assert.Equal(blockRoot + @"\SomeVerb", call.SubKey);
    }

    [Fact]
    public void Empty_block_names_are_ignored_without_a_transaction()
    {
        var sink = new RecordingTransactor();
        var svc = new Win11MenuService(@"Software\RCMenuManager.Tests\T\x",
                                        @"Software\RCMenuManager.Tests\B\x", sink);

        svc.RemoveFromBlock("");
        svc.RemoveFromBlock("   ");

        Assert.Empty(sink.Calls);
    }

    [Fact]
    public void Without_a_transactor_the_service_still_works_for_scratch_keys()
    {
        // The parameterless test path writes directly; production always injects one.
        var root = @"Software\RCMenuManager.Tests\Direct\" + Guid.NewGuid().ToString("N");
        var svc = new Win11MenuService(root, @"Software\RCMenuManager.Tests\DirectB\x");
        try
        {
            svc.SetNewMenuEnabled(false);
            Assert.False(svc.IsNewMenuEnabled);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false); } catch { }
        }
    }
}

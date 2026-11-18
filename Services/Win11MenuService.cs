using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>
/// Win11 新菜单 / 经典菜单切换与 Block 列表管理。
/// <para>
/// Every write goes through <see cref="IRegistryTransactor"/>. The toggle deletes a
/// whole CLSID tree — exactly the kind of change the product promises is always
/// backed up and always logged — so it must not write to the registry directly the
/// way it used to. Reads stay direct: they have no side effects.
/// </para>
/// </summary>
public sealed class Win11MenuService : IWin11MenuService
{
    private const string DefaultToggleRoot = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string DefaultBlockRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell\Block";
    private const string InprocSubKey = "InprocServer32";

    /// <summary>Scope label used for these writes in the operation log.</summary>
    private const string ScopeId = "Win11";

    private readonly string _toggleRoot;
    private readonly string _blockRoot;
    private readonly IRegistryTransactor? _transactor;

    public Win11MenuService(IRegistryTransactor transactor)
        : this(DefaultToggleRoot, DefaultBlockRoot, transactor) { }

    /// <summary>
    /// Test constructor. Without a transactor the writes go straight to the registry,
    /// which is what the scratch-key tests exercise.
    /// </summary>
    public Win11MenuService(string toggleRoot, string blockRoot, IRegistryTransactor? transactor = null)
    {
        _toggleRoot = toggleRoot;
        _blockRoot = blockRoot;
        _transactor = transactor;
    }

    public bool IsNewMenuEnabled => !KeyExists(_toggleRoot);

    public void SetNewMenuEnabled(bool enabled)
    {
        var inprocPath = _toggleRoot + @"\" + InprocSubKey;
        var exists = KeyExists(_toggleRoot);
        if (enabled && exists)
        {
            Write(_toggleRoot, "Win11Menu", "EnableWin11Menu",
                w => w.DeleteSubKeyTree(RegistryHive.CurrentUser, _toggleRoot));
        }
        else if (!enabled && !exists)
        {
            Write(inprocPath, "Win11Menu", "DisableWin11Menu", w =>
            {
                w.CreateSubKey(RegistryHive.CurrentUser, inprocPath);
                w.SetStringValue(RegistryHive.CurrentUser, inprocPath, string.Empty, string.Empty);
            });
        }
    }

    public IReadOnlyList<Models.Win11BlockItem> GetBlockList()
    {
        var list = new List<Models.Win11BlockItem>();
        using var root = Registry.CurrentUser.OpenSubKey(_blockRoot, writable: false);
        if (root is null) return list;
        foreach (var name in root.GetSubKeyNames())
            list.Add(new Models.Win11BlockItem(name));
        return list;
    }

    public void RemoveFromBlock(string verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName)) return;
        var target = _blockRoot + @"\" + verbName;
        Write(target, verbName, "RemoveBlock",
            w => w.DeleteSubKeyTree(RegistryHive.CurrentUser, target));
    }

    public void RestartExplorer()
    {
        var kill = new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
            Arguments = "/f /im explorer.exe",
        };
        using (var p = Process.Start(kill))
        {
            p?.WaitForExit(3000);
        }
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
    }

    private void Write(string subKey, string verbName, string op, Action<IRegistryWriter> mutate)
    {
        if (_transactor is not null)
        {
            _transactor.Run(RegistryHive.CurrentUser, subKey, ScopeId, verbName, op, mutate);
            return;
        }
        mutate(new Win32RegistryWriter());
    }

    private static bool KeyExists(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return k is not null;
    }
}

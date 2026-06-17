using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace RCMenuManager.Services;

public sealed class Win11MenuService : IWin11MenuService
{
    private const string DefaultToggleRoot = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string DefaultBlockRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell\Block";
    private const string InprocSubKey = "InprocServer32";

    private readonly string _toggleRoot;
    private readonly string _blockRoot;

    public Win11MenuService() : this(DefaultToggleRoot, DefaultBlockRoot) { }

    public Win11MenuService(string toggleRoot, string blockRoot)
    {
        _toggleRoot = toggleRoot;
        _blockRoot = blockRoot;
    }

    public bool IsNewMenuEnabled => !KeyExists(_toggleRoot);

    public void SetNewMenuEnabled(bool enabled)
    {
        var inprocPath = _toggleRoot + @"\" + InprocSubKey;
        var exists = KeyExists(_toggleRoot);
        if (enabled && exists)
        {
            DeleteTree(_toggleRoot);
        }
        else if (!enabled && !exists)
        {
            CreateKey(inprocPath);
            SetDefault(inprocPath, "");
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
        DeleteTree(_blockRoot + @"\" + verbName);
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

    private static bool KeyExists(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return k is not null;
    }

    private static void CreateKey(string subKey)
    {
        using var k = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
    }

    private static void SetDefault(string subKey, string value)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        k?.SetValue("", value, RegistryValueKind.String);
    }

    private static void DeleteTree(string subKey)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }
}
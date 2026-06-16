using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>
/// Exports a registry sub-key tree to a .reg file using reg.exe export. The
/// export is the safety net before any verb mutation: if it fails, the caller
/// must abort the write.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly string _backupDir;

    public BackupService() : this(DefaultBackupDir()) { }

    public BackupService(string backupDir)
    {
        _backupDir = backupDir;
        Directory.CreateDirectory(_backupDir);
    }

    public static string DefaultBackupDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "backups");

    /// <summary>
    /// Exports <paramref name="hive"/>\<paramref name="subKey"/> to a .reg file
    /// inside the backup directory. Returns the absolute file path.
    /// </summary>
    public string Export(RegistryHive hive, string subKey, string scopeId, string verbName)
    {
        EnsureKeyExists(hive, subKey);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeScope = Sanitize(scopeId);
        var safeVerb = Sanitize(verbName);
        var fileName = $"{stamp}-{safeScope}-{safeVerb}.reg";
        var fullPath = Path.Combine(_backupDir, fileName);

        var fullKeyPath = $"{HiveDisplayName(hive)}\\{subKey}";
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("export");
        psi.ArgumentList.Add(fullKeyPath);
        psi.ArgumentList.Add(fullPath);
        psi.ArgumentList.Add("/y");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 reg.exe");
        proc.WaitForExit();
        if (proc.ExitCode != 0 || !File.Exists(fullPath))
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"reg.exe export 失败 (exit={proc.ExitCode}): {err}");
        }
        return fullPath;
    }

    private static void EnsureKeyExists(RegistryHive hive, string subKey)
    {
        using var root = hive switch
        {
            RegistryHive.ClassesRoot => Registry.ClassesRoot,
            RegistryHive.CurrentUser => Registry.CurrentUser,
            RegistryHive.LocalMachine => Registry.LocalMachine,
            _ => throw new ArgumentOutOfRangeException(nameof(hive)),
        };
        using var key = root.OpenSubKey(subKey);
        if (key is null) throw new InvalidOperationException($"目标键不存在：{HiveDisplayName(hive)}\\{subKey}");
    }

    private static string HiveDisplayName(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.Users => "HKU",
        _ => hive.ToString(),
    };

    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}

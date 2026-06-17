using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public sealed class BackupService : IBackupService
{
    private readonly string _backupDir;

    public BackupService() : this(DefaultBackupDir()) { }
    public BackupService(string backupDir) { _backupDir = backupDir; Directory.CreateDirectory(_backupDir); }

    public static string DefaultBackupDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "backups");

    public string Export(RegistryHive hive, string subKey, string scopeId, string verbName)
    {
        EnsureKeyExists(hive, subKey);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{stamp}-{Sanitize(scopeId)}-{Sanitize(verbName)}.reg";
        var fullPath = Path.Combine(_backupDir, fileName);
        var fullKeyPath = $"{HiveDisplayName(hive)}\\{subKey}";
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
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

    public IReadOnlyList<BackupRecord> List()
    {
        if (!Directory.Exists(_backupDir)) return Array.Empty<BackupRecord>();
        var records = new List<BackupRecord>();
        foreach (var f in Directory.EnumerateFiles(_backupDir, "*.reg"))
        {
            var r = BackupRecord.FromFile(f, Array.Empty<OperationLogEntry>());
            if (r is not null) records.Add(r);
        }
        records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return records;
    }

    public void Import(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("备份文件不存在", filePath);
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("import");
        psi.ArgumentList.Add(filePath);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 reg.exe");
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"reg.exe import 失败 (exit={proc.ExitCode}): {err}");
        }
    }

    public void Delete(string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
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
        foreach (var c in raw) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}

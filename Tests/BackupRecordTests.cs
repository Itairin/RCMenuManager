using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class BackupRecordTests
{
    [Fact]
    public void FromFile_parses_full_filename()
    {
        var rec = BackupRecord.FromFile(@"C:\b\20260617-142530-Folder-OpenWith.reg", Array.Empty<OperationLogEntry>());
        Assert.NotNull(rec);
        Assert.Equal(new DateTime(2026, 6, 17, 14, 25, 30), rec!.Timestamp);
        Assert.Equal("Folder", rec.ScopeId);
        Assert.Equal("OpenWith", rec.VerbName);
    }

    [Fact]
    public void FromFile_parses_empty_scope()
    {
        var rec = BackupRecord.FromFile(@"C:\b\20260617-142530--OpenWith.reg", Array.Empty<OperationLogEntry>());
        Assert.NotNull(rec);
        Assert.Equal(string.Empty, rec!.ScopeId);
        Assert.Equal("OpenWith", rec.VerbName);
    }

    [Fact]
    public void FromFile_parses_sequence_token_used_for_same_second_writes()
    {
        var rec = BackupRecord.FromFile(@"C:\b\20260617-142530-002-Folder-OpenWith.reg", Array.Empty<OperationLogEntry>());
        Assert.NotNull(rec);
        Assert.Equal(new DateTime(2026, 6, 17, 14, 25, 30), rec!.Timestamp);
        Assert.Equal("Folder", rec.ScopeId);
        Assert.Equal("OpenWith", rec.VerbName);
    }

    [Fact]
    public void FromFile_rejects_non_reg() => Assert.Null(BackupRecord.FromFile(@"C:\b\foo.txt", Array.Empty<OperationLogEntry>()));

    [Fact]
    public void FromFile_rejects_short_filename() => Assert.Null(BackupRecord.FromFile(@"C:\b\abc.reg", Array.Empty<OperationLogEntry>()));

    [Fact]
    public void FromFile_rejects_bad_timestamp() => Assert.Null(BackupRecord.FromFile(@"C:\b\abcdefghijklmnop-Foo-Bar.reg", Array.Empty<OperationLogEntry>()));

    [Fact]
    public void FromFile_reads_the_registry_path_from_a_utf16_reg_header()
    {
        // This is the case that used to break after log rotation: the backup is on
        // disk, but no log line points at it.
        var path = WriteReg(Encoding.Unicode,
            "Windows Registry Editor Version 5.00",
            "",
            @"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\VSCode]",
            @"""Icon""=""C:\\Tools\\Code.exe""");

        var rec = BackupRecord.FromFile(path, Array.Empty<OperationLogEntry>());

        Assert.NotNull(rec);
        Assert.Equal(@"HKCU\Software\Classes\Directory\shell\VSCode", rec!.RegistryPath);
        Assert.Equal("未知", rec.Operation);
    }

    [Fact]
    public void FromFile_reads_the_registry_path_from_a_utf8_reg_header()
    {
        var path = WriteReg(new UTF8Encoding(false),
            "Windows Registry Editor Version 5.00",
            "",
            @"[HKEY_LOCAL_MACHINE\SOFTWARE\Classes\*\shell\OpenWith]");

        var rec = BackupRecord.FromFile(path, Array.Empty<OperationLogEntry>());

        Assert.Equal(@"HKLM\SOFTWARE\Classes\*\shell\OpenWith", rec!.RegistryPath);
    }

    [Fact]
    public void FromFile_prefers_the_log_entry_over_the_header()
    {
        var path = WriteReg(Encoding.Unicode,
            "Windows Registry Editor Version 5.00", "",
            @"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LogWins]");
        var log = new List<OperationLogEntry>
        {
            new(DateTime.UtcNow, "Folder", "LogWins", "Disable",
                Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\LogWins",
                path, success: true, error: null),
        };

        var rec = BackupRecord.FromFile(path, log);

        Assert.Equal(@"HKLM\SOFTWARE\Classes\Directory\shell\LogWins", rec!.RegistryPath);
        Assert.Equal("Disable", rec.Operation);
    }

    [Fact]
    public void FromFile_leaves_the_path_null_when_the_file_is_missing()
    {
        var rec = BackupRecord.FromFile(
            Path.Combine(Path.GetTempPath(), "20260617-142530-Folder-Ghost.reg"),
            Array.Empty<OperationLogEntry>());

        Assert.NotNull(rec);
        Assert.Null(rec!.RegistryPath);
    }

    private static string WriteReg(Encoding encoding, params string[] lines)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "20260617-142530-Folder-Backup.reg");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", encoding);
        return path;
    }

    [Fact]
    public void FromFile_cross_references_log_by_backupPath()
    {
        var path = @"C:\b\20260617-142530-Folder-OpenWith.reg";
        var log = new List<OperationLogEntry>
        {
            new(DateTime.UtcNow, "Folder", "OpenWith", "CreateRoot",
                Microsoft.Win32.RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\OpenWith",
                path, success: true, error: null),
        };
        var rec = BackupRecord.FromFile(path, log);
        Assert.NotNull(rec);
        Assert.Equal("CreateRoot", rec!.Operation);
        Assert.True(rec.Success);
    }
}

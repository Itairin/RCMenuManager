using System;
using System.Collections.Generic;
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
    public void FromFile_rejects_non_reg() => Assert.Null(BackupRecord.FromFile(@"C:\b\foo.txt", Array.Empty<OperationLogEntry>()));

    [Fact]
    public void FromFile_rejects_short_filename() => Assert.Null(BackupRecord.FromFile(@"C:\b\abc.reg", Array.Empty<OperationLogEntry>()));

    [Fact]
    public void FromFile_rejects_bad_timestamp() => Assert.Null(BackupRecord.FromFile(@"C:\b\abcdefghijklmnop-Foo-Bar.reg", Array.Empty<OperationLogEntry>()));

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

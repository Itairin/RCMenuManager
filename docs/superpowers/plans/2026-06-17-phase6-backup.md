# Phase 6: 备份与日志 Implementation Plan

> REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 主窗口顶部加"备份"按钮 -> 弹出 `BackupDialog`：备份 Tab 列出 .reg 备份（还原/删除/打开目录）；日志 Tab 列出 `operations.log` 每行 JSON。顺手修 `BackupService.cs` 里的中文 mojibake。

**Architecture:** `BackupService` / `OperationLogService` 扩方法；`BackupRecord` 从文件名 + log 交叉解析；`BackupDialogViewModel` 装填两个集合 + 命令；`MainViewModel.ShowBackupsCommand` 打开弹窗；弹窗内"还原"通过回调让 `MainViewModel` 负责 UAC + 真正导入 + 刷作用域。

**Tech Stack:** WPF / .NET 9 / CommunityToolkit.Mvvm

**Spec:** `docs/superpowers/specs/2026-06-17-phase6-backup-design.md`

---

## Task 1: Services 增强（修中文 + 加新方法 + 改接口）

**Files:** Modify `Services/BackupService.cs`, `IBackupService.cs`, `OperationLogService.cs`, `IOperationLog.cs`

### Step 1: 改 IBackupService

`Services/IBackupService.cs` 全文替换：

```csharp
using System.Collections.Generic;
using Microsoft.Win32;

namespace RCMenuManager.Services;

public interface IBackupService
{
    string Export(RegistryHive hive, string subKey, string scopeId, string verbName);
    IReadOnlyList<Models.BackupRecord> List();
    void Import(string filePath);
    void Delete(string filePath);
}
```

### Step 2: 改 BackupService（修中文 + 加 List/Import/Delete）

`Services/BackupService.cs` **整体替换**为：

```csharp
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
```

### Step 3: 改 IOperationLog

`Services/IOperationLog.cs` 全文替换：

```csharp
using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IOperationLog
{
    void Append(OperationLogEntry entry);
    IReadOnlyList<OperationLogEntry> ReadAll();
}
```

### Step 4: 给 OperationLogService 加 ReadAll

在 `Services/OperationLogService.cs` 的 `Append` 方法之后追加：

```csharp
    public IReadOnlyList<OperationLogEntry> ReadAll()
    {
        if (!File.Exists(_path)) return Array.Empty<OperationLogEntry>();
        var list = new List<OperationLogEntry>();
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { list.Add(JsonSerializer.Deserialize<OperationLogEntry>(line)!); }
            catch { }
        }
        return list;
    }
```

（顶部 `using` 区域追加 `using System.Collections.Generic;`）

### Step 5: 验证构建 + 测试 + 提交

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" build RCMenuManager.sln --nologo -v:m
& "C:\Users\chen7\.dotnet\dotnet.exe" test RCMenuManager.sln --nologo -v:m
git add Services/BackupService.cs Services/IBackupService.cs Services/OperationLogService.cs Services/IOperationLog.cs
git commit -m "feat: extend backup and log services with list/import/delete/read"
```

预期：构建通过；26 个测试仍全过。
## Task 2: BackupRecord 模型

**Files:** Create `Models/BackupRecord.cs`

`Models/BackupRecord.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RCMenuManager.Services;

namespace RCMenuManager.Models;

public sealed record BackupRecord(
    string FilePath,
    DateTime Timestamp,
    string ScopeId,
    string VerbName,
    string Operation,
    string? RegistryPath,
    bool? Success,
    string? Error)
{
    public string SuccessText => Success switch
    {
        true => "成功",
        false => "失败",
        _ => "未知",
    };

    public static BackupRecord? FromFile(string filePath, IReadOnlyList<OperationLogEntry> log)
    {
        var fileName = Path.GetFileName(filePath);
        if (!fileName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) return null;
        var stem = fileName.Substring(0, fileName.Length - 4);
        const int tsLen = 15;
        if (stem.Length < tsLen || stem[8] != '-') return null;
        if (!DateTime.TryParseExact(stem.Substring(0, tsLen), "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var ts)) return null;
        var rest = stem.Length > tsLen + 1 ? stem.Substring(tsLen + 1) : string.Empty;
        string scope, verb;
        var lastDash = rest.LastIndexOf('-');
        if (lastDash < 0) { scope = string.Empty; verb = rest; }
        else { scope = rest.Substring(0, lastDash); verb = rest.Substring(lastDash + 1); }
        var match = log.FirstOrDefault(e => e.backupPath == filePath);
        return new BackupRecord(
            FilePath: filePath, Timestamp: ts, ScopeId: scope, VerbName: verb,
            Operation: match?.op ?? "未知",
            RegistryPath: match is null ? null : $"{match.hive}\\{match.subKey}",
            Success: match?.success, Error: match?.error);
    }

    public static IReadOnlyList<BackupRecord> ReadDirectory(string dir, IReadOnlyList<OperationLogEntry> log)
    {
        if (!Directory.Exists(dir)) return Array.Empty<BackupRecord>();
        var records = new List<BackupRecord>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.reg"))
        {
            var r = FromFile(f, log);
            if (r is not null) records.Add(r);
        }
        records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return records;
    }
}
```

提交：

```powershell
git add Models/BackupRecord.cs
git commit -m "feat: add BackupRecord model with filename parser"
```

---

## Task 3: 单元测试

**Files:** Create `Tests/BackupRecordTests.cs`, `Tests/OperationLogServiceTests.cs`; Modify `Tests/BackupServiceTests.cs`

### Step 1: BackupRecordTests

```csharp
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
```

### Step 2: OperationLogServiceTests

```csharp
using System;
using System.IO;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class OperationLogServiceTests
{
    [Fact]
    public void ReadAll_returns_empty_when_file_missing()
    {
        var svc = new OperationLogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "op.log"));
        Assert.Empty(svc.ReadAll());
    }

    [Fact]
    public void ReadAll_skips_corrupt_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"), "op.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "{\"timestamp\":\"2026-06-17T10:00:00Z\",\"scopeId\":\"Folder\",\"verb\":\"x\",\"op\":\"Disable\",\"hive\":0,\"subKey\":\"a\",\"backupPath\":null,\"success\":true,\"error\":null}" + Environment.NewLine
            + "{ this is not valid json" + Environment.NewLine);
        var svc = new OperationLogService(path);
        Assert.Single(svc.ReadAll());
    }
}
```

### Step 3: 补充 BackupServiceTests

`Tests/BackupServiceTests.cs` 末尾追加：

```csharp
public class BackupServiceListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RCMenuManagerBackupListTests", Guid.NewGuid().ToString("N"));
    public BackupServiceListTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void List_returns_records_newest_first_and_ignores_non_reg()
    {
        File.WriteAllText(Path.Combine(_dir, "20260617-142530-Folder-A.reg"), "x");
        File.WriteAllText(Path.Combine(_dir, "20260617-150000-Folder-B.reg"), "x");
        File.WriteAllText(Path.Combine(_dir, "ignore.txt"), "noise");
        var list = new BackupService(_dir).List();
        Assert.Equal(2, list.Count);
        Assert.Equal("B", list[0].VerbName);
        Assert.Equal("A", list[1].VerbName);
    }

    [Fact]
    public void List_returns_empty_when_dir_missing() => Assert.Empty(new BackupService(Path.Combine(_dir, "nope")).List());

    [Fact]
    public void Delete_removes_existing_file()
    {
        var path = Path.Combine(_dir, "20260617-142530-Folder-A.reg");
        File.WriteAllText(path, "x");
        new BackupService(_dir).Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_silent_on_missing() => new BackupService(_dir).Delete(Path.Combine(_dir, "never-existed.reg"));
}
```

提交：

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" test RCMenuManager.sln --nologo -v:m
git add Tests/BackupRecordTests.cs Tests/OperationLogServiceTests.cs Tests/BackupServiceTests.cs
git commit -m "test: cover BackupRecord parsing, list/delete, and log read"
```

---

## Task 4: ViewModels

**Files:** Create `Models/OperationLogEntryViewModel.cs`, `ViewModels/BackupDialogViewModel.cs`

`Models/OperationLogEntryViewModel.cs`：

```csharp
using System;
using RCMenuManager.Services;

namespace RCMenuManager.Models;

public sealed class OperationLogEntryViewModel
{
    public DateTime Timestamp { get; }
    public string Op { get; }
    public string ScopeId { get; }
    public string Verb { get; }
    public string Hive { get; }
    public string SubKey { get; }
    public string SuccessText { get; }
    public string? Error { get; }

    public OperationLogEntryViewModel(OperationLogEntry e)
    {
        Timestamp = e.timestamp;
        Op = e.op;
        ScopeId = e.scopeId;
        Verb = e.verb;
        Hive = e.hive.ToString();
        SubKey = e.subKey;
        SuccessText = e.success ? "成功" : "失败";
        Error = e.error;
    }
}
```

`ViewModels/BackupDialogViewModel.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public partial class BackupDialogViewModel : ObservableObject
{
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;

    public ObservableCollection<BackupRecord> Backups { get; } = new();
    public ObservableCollection<OperationLogEntryViewModel> Operations { get; } = new();
    public string BackupDir { get; }

    [ObservableProperty] private BackupRecord? _selectedBackup;
    [ObservableProperty] private OperationLogEntryViewModel? _selectedOperation;
    [ObservableProperty] private string _statusText = "正在加载...";

    public bool HasSelection => SelectedBackup is not null;
    public Func<BackupRecord, Task<bool>>? OnRestoreRequested { get; set; }

    public BackupDialogViewModel(IBackupService backup, IOperationLog log, string backupDir)
    {
        _backup = backup; _log = log; BackupDir = backupDir; Refresh();
    }

    public void Refresh()
    {
        var log = _log.ReadAll();
        var logByPath = log.Where(e => !string.IsNullOrEmpty(e.backupPath))
            .GroupBy(e => e.backupPath!).ToDictionary(g => g.Key, g => g.Last());

        Backups.Clear();
        foreach (var rec in _backup.List())
        {
            if (logByPath.TryGetValue(rec.FilePath, out var e))
                Backups.Add(rec with { Operation = e.op, RegistryPath = $"{e.hive}\\{e.subKey}", Success = e.success, Error = e.error });
            else Backups.Add(rec);
        }

        Operations.Clear();
        foreach (var e in log.AsEnumerable().Reverse())
            Operations.Add(new OperationLogEntryViewModel(e));

        StatusText = $"共 {Backups.Count} 条备份 / {Operations.Count} 条日志";
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task RestoreAsync(BackupRecord? rec)
    {
        if (rec is null || OnRestoreRequested is null) return;
        var ok = await OnRestoreRequested(rec);
        if (ok) Refresh();
    }

    [RelayCommand]
    private void DeleteBackup(BackupRecord? rec)
    {
        if (rec is null) return;
        _backup.Delete(rec.FilePath);
        Refresh();
    }

    [RelayCommand]
    private void OpenBackupFolder()
        => Process.Start(new ProcessStartInfo { FileName = BackupDir, UseShellExecute = true });

    [RelayCommand]
    private void RevealInExplorer(BackupRecord? rec)
    {
        if (rec is null) return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{rec.FilePath}\"" });
    }

    partial void OnSelectedBackupChanged(BackupRecord? value) => OnPropertyChanged(nameof(HasSelection));
}
```

提交：

```powershell
git add Models/OperationLogEntryViewModel.cs ViewModels/BackupDialogViewModel.cs
git commit -m "feat: add BackupDialogViewModel and OperationLogEntryViewModel"
```
## Task 5: BackupDialog XAML + converter

**Files:** Create `Views/Dialogs/BackupDialog.xaml`, `BackupDialog.xaml.cs`, `Converters/CanRestoreConverter.cs`

`Converters/CanRestoreConverter.cs`：

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace RCMenuManager.Converters;

public sealed class CanRestoreConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        var success = values[0] is bool b && b;
        var path = values[1] as string;
        return success && !string.IsNullOrEmpty(path);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

`Views/Dialogs/BackupDialog.xaml`：

```xml
<Window x:Class="RCMenuManager.Views.Dialogs.BackupDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:conv="clr-namespace:RCMenuManager.Converters"
        Title="备份与日志" Width="960" Height="600"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        FontFamily="Segoe UI, Microsoft YaHei UI, sans-serif" FontSize="13">
    <Window.Resources>
        <conv:CanRestoreConverter x:Key="CanRestoreConv" />
    </Window.Resources>
    <DockPanel Margin="12">
        <DockPanel DockPanel.Dock="Bottom" Margin="0,10,0,0">
            <Button DockPanel.Dock="Right" Content="关闭" Width="80" Height="28" Margin="6,0,0,0"
                    IsCancel="True" Click="CloseButton_Click" />
            <Button DockPanel.Dock="Right" Content="打开备份目录" Width="100" Height="28"
                    Command="{Binding OpenBackupFolderCommand}" />
            <TextBlock Text="{Binding StatusText}" Foreground="#5F6B7A" FontSize="12"
                       VerticalAlignment="Center" />
        </DockPanel>

        <TabControl>
            <TabItem Header="备份">
                <DataGrid ItemsSource="{Binding Backups}" SelectedItem="{Binding SelectedBackup}"
                          AutoGenerateColumns="False" IsReadOnly="True"
                          HeadersVisibility="Column" GridLinesVisibility="Horizontal"
                          RowHeaderWidth="0" AlternatingRowBackground="#FAFBFC">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="时间" Binding="{Binding Timestamp, StringFormat=yyyy-MM-dd HH:mm:ss}" Width="150" />
                        <DataGridTextColumn Header="作用域" Binding="{Binding ScopeId}" Width="120" />
                        <DataGridTextColumn Header="Verb" Binding="{Binding VerbName}" Width="160" />
                        <DataGridTextColumn Header="操作" Binding="{Binding Operation}" Width="90" />
                        <DataGridTextColumn Header="状态" Binding="{Binding SuccessText}" Width="80" />
                        <DataGridTextColumn Header="注册表" Binding="{Binding RegistryPath}" Width="280" />
                        <DataGridTemplateColumn Header="操作" Width="220">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal">
                                        <Button Content="还原" Width="50" Height="24" Margin="2,0"
                                                Command="{Binding DataContext.RestoreCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                CommandParameter="{Binding}">
                                            <Button.IsEnabled>
                                                <MultiBinding Converter="{StaticResource CanRestoreConv}">
                                                    <Binding Path="Success" />
                                                    <Binding Path="RegistryPath" />
                                                </MultiBinding>
                                            </Button.IsEnabled>
                                        </Button>
                                        <Button Content="删除" Width="50" Height="24" Margin="2,0"
                                                Command="{Binding DataContext.DeleteBackupCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                CommandParameter="{Binding}" />
                                        <Button Content="定位" Width="50" Height="24" Margin="2,0"
                                                Command="{Binding DataContext.RevealInExplorerCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                CommandParameter="{Binding}" />
                                    </StackPanel>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </TabItem>

            <TabItem Header="日志">
                <DataGrid ItemsSource="{Binding Operations}" SelectedItem="{Binding SelectedOperation}"
                          AutoGenerateColumns="False" IsReadOnly="True"
                          HeadersVisibility="Column" GridLinesVisibility="Horizontal"
                          RowHeaderWidth="0" AlternatingRowBackground="#FAFBFC">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="时间" Binding="{Binding Timestamp, StringFormat=yyyy-MM-dd HH:mm:ss}" Width="150" />
                        <DataGridTextColumn Header="操作" Binding="{Binding Op}" Width="100" />
                        <DataGridTextColumn Header="作用域" Binding="{Binding ScopeId}" Width="100" />
                        <DataGridTextColumn Header="Verb" Binding="{Binding Verb}" Width="140" />
                        <DataGridTextColumn Header="Hive" Binding="{Binding Hive}" Width="70" />
                        <DataGridTextColumn Header="SubKey" Binding="{Binding SubKey}" Width="*" />
                        <DataGridTextColumn Header="状态" Binding="{Binding SuccessText}" Width="80" />
                        <DataGridTextColumn Header="错误" Binding="{Binding Error}" Width="200" />
                    </DataGrid.Columns>
                </DataGrid>
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```

`Views/Dialogs/BackupDialog.xaml.cs`：

```csharp
using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class BackupDialog : Window
{
    public BackupDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
```

提交：

```powershell
git add Views/Dialogs/BackupDialog.xaml Views/Dialogs/BackupDialog.xaml.cs Converters/CanRestoreConverter.cs
git commit -m "feat: add BackupDialog UI with backup and log tabs"
```

---

## Task 6: MainViewModel + ScopeBar 改造

**Files:** Modify `ViewModels/MainViewModel.cs`, `Views/Controls/ScopeBar.xaml`

### MainViewModel 顶部加 using

```csharp
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Views.Dialogs;
```

### 字段区加

```csharp
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;
```

### 构造函数加参数 + 赋值

`IBackupService backup, IOperationLog log` 形参，构造体内加 `_backup = backup; _log = log;`。DI 已注册。

### 加命令

```csharp
    [RelayCommand]
    private void ShowBackups()
    {
        var owner = Application.Current?.MainWindow;
        var vm = new BackupDialogViewModel(_backup, _log, BackupService.DefaultBackupDir())
        {
            OnRestoreRequested = HandleRestoreAsync,
        };
        var dlg = new BackupDialog { Owner = owner, DataContext = vm };
        dlg.ShowDialog();
    }

    private async Task<bool> HandleRestoreAsync(BackupRecord rec)
    {
        foreach (Window w in Application.Current!.Windows)
            if (w is BackupDialog) { w.Close(); break; }

        if (rec.RegistryPath is null)
        {
            StatusText = "无法恢复：该备份没有关联的操作日志记录";
            return false;
        }

        var ok = ConfirmDialog.Show(
            "恢复备份",
            $"将导入备份文件 {System.IO.Path.GetFileName(rec.FilePath)}，覆盖当前注册表项：\n{rec.RegistryPath}\n\n该操作不可撤销，请确认。",
            confirmText: "恢复", isDestructive: true);
        if (!ok) return false;

        var (hive, subKey) = ParseRegistryPath(rec.RegistryPath);
        if (!await EnsureAdministratorAsync(hive, subKey)) return false;

        try
        {
            _backup.Import(rec.FilePath);
            StatusText = $"已恢复 {rec.VerbName}";
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"恢复失败：{ex.Message}";
            return false;
        }
    }

    private static (RegistryHive hive, string subKey) ParseRegistryPath(string registryPath)
    {
        var idx = registryPath.IndexOf('\\');
        if (idx < 0) return (RegistryHive.CurrentUser, registryPath);
        var hiveName = registryPath.Substring(0, idx);
        var subKey = registryPath.Substring(idx + 1);
        var hive = hiveName switch
        {
            "HKCU" => RegistryHive.CurrentUser,
            "HKLM" => RegistryHive.LocalMachine,
            "HKCR" => RegistryHive.ClassesRoot,
            "HKU" => RegistryHive.Users,
            _ => RegistryHive.CurrentUser,
        };
        return (hive, subKey);
    }
```

### ScopeBar.xaml 整体替换

```xml
<UserControl x:Class="RCMenuManager.Views.Controls.ScopeBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="320" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="160" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="作用域" FontWeight="SemiBold" Foreground="#1F2937"
                   VerticalAlignment="Center" />
        <ComboBox Grid.Column="1" Margin="8,0,0,0"
                  ItemsSource="{Binding Scopes}"
                  SelectedItem="{Binding SelectedScope, Mode=TwoWay}"
                  DisplayMemberPath="Label" />
        <TextBlock Grid.Column="2" Text="自定义扩展名" FontWeight="SemiBold" Foreground="#1F2937"
                   Margin="20,0,0,0" VerticalAlignment="Center" />
        <TextBox Grid.Column="3" Margin="8,0,0,0"
                 Text="{Binding CustomExtensionInput, UpdateSourceTrigger=PropertyChanged}"
                 VerticalContentAlignment="Center"
                 ToolTip="例如 .txt 或 txt" />
        <Button Grid.Column="4" Content="加载" Margin="6,0,0,0" Padding="12,4"
                Command="{Binding LoadCustomExtensionCommand}" />
        <Button Grid.Column="5" Content="备份" Margin="6,0,8,0" Padding="12,4"
                HorizontalAlignment="Right"
                Command="{Binding ShowBackupsCommand}" />
        <Button Grid.Column="6" Content="刷新" Padding="12,4"
                Command="{Binding RefreshCommand}" />
    </Grid>
</UserControl>
```

提交：

```powershell
git add ViewModels/MainViewModel.cs Views/Controls/ScopeBar.xaml
git commit -m "feat: wire ShowBackupsCommand and add backup button to scope bar"
```

---

## Task 7: smoke 清单

`docs/superpowers/smoke/2026-06-17-phase6-smoke.md`：

```markdown
# Phase 6 手动 Smoke 测试

本阶段新增"备份"弹窗（备份 Tab + 日志 Tab）。下列步骤不写 HKLM。

## 1. 打开弹窗
1. 启动 RCMenuManager。
2. 顶部应新增"备份"按钮（位于"加载"和"刷新"之间）。
3. 点击"备份"-> 弹出"备份与日志"对话框（960x600）。

## 2. 备份 Tab
1. 若已有备份，列表按时间倒序显示。
2. 若为空：先新增一个 verb，再点"备份" -> 列表多出 1 条。
3. 列：时间 / 作用域 / Verb / 操作 / 状态 / 注册表路径。
4. 行内 3 个按钮：还原 / 删除 / 定位。

## 3. 日志 Tab
1. 切到"日志"Tab。
2. 显示 `operations.log` 全部记录，倒序。
3. 列：时间 / 操作 / 作用域 / Verb / Hive / SubKey / 状态 / 错误。

## 4. 还原（仅成功项）
1. 备份 Tab 选一条"成功"项。
2. 点"还原"-> 弹窗自动关闭 -> 弹出"恢复备份"确认框。
3. 取消 -> 无变化。
4. 重新选 -> 确认 -> 秒级完成（HKCU），状态栏"已恢复"。

## 5. 删除
1. 选任意项 -> 点"删除"。
2. 列表立即移除，文件从 `%LOCALAPPDATA%\RCMenuManager\backups` 消失。

## 6. 打开备份目录
1. 底部"打开备份目录" -> 资源管理器打开 backups 目录。

## 7. 定位单文件
1. 行内"定位" -> 资源管理器打开并高亮该 .reg。

## 8. 失败项不可还原
1. 选 SuccessText=失败的项（若有）-> "还原"按钮禁用。

## 9. 关闭
1. 底部"关闭"或 X -> 弹窗关闭，无副作用。
2. 再次打开 -> 内容已刷新到最新。
```

```powershell
git add docs/superpowers/smoke/2026-06-17-phase6-smoke.md
git commit -m "docs: phase 6 manual smoke checklist"
```

---

## 自审

- Spec §4.1-§4.4 -> Task 1, 2
- Spec §4.5-§4.6 -> Task 3-5
- Spec §4.7-§4.8 -> Task 6
- Spec §8 测试 -> Task 3
- async 死锁 -> `HandleRestoreAsync` 全部用 `await`

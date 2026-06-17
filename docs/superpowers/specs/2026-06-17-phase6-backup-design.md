# Phase 6: 备份与日志 设计文档

## 1. 目标

补齐 Phase 3 留下的"备份恢复 + 操作日志"UI。用户在主窗口顶部一键打开 `BackupDialog`：
- 备份 Tab：以表格列出所有历史 .reg 备份，附 [还原] / [删除] / [打开所在文件夹] 三个动作。
- 日志 Tab：只读表格列出 `operations.log` 的每条 JSON 记录。

顺手修复 `BackupService.cs` 里的中文 mojibake（旧会话写入编码错乱）。

## 2. 范围

**In scope：**
- `IBackupService` 新增 `List()` / `Import(path)` / `Delete(path)` 三个方法。
- `IOperationLog` 新增 `ReadAll()` 方法。
- `BackupService` 修复中文 mojibake，实现新方法。
- `OperationLogService` 实现 `ReadAll()`。
- 新增 `Models/BackupRecord.cs`：从 .reg 文件名解析 timestamp / scope / verb，并交叉 `operations.log` 拿成功/失败/错误。
- 新增 `ViewModels/BackupDialogViewModel.cs`：列表 + 选中 + 三个命令。
- 新增 `Views/Dialogs/BackupDialog.xaml` + code-behind：双 Tab 对话框。
- `MainViewModel` 加 `ShowBackupsCommand`；`ScopeBar.xaml` 加一个"备份"按钮触发。
- 新增单元测试：`BackupRecord` 解析、`BackupService.List` / `Delete` 行为（不测 `Import`，因为需要真实 reg.exe）。

**Out of scope：**
- 系统还原点（dev doc 列为可选，本期不做）。
- 自动清理过期备份（YAGNI）。
- `reg.exe import` 的进程内 Win32 调用包装层（直接 `Process.Start` 即可，与 `Export` 一致）。

## 3. 架构

```
ScopeBar 顶部加"备份"按钮
   └─ ShowBackupsCommand → BackupDialog.Show(Application.Current.MainWindow)
        └─ BackupDialog 持有 BackupDialogViewModel
             ├─ TabControl
             │    ├─ TabItem "备份"   → DataGrid<BackupRecord>  + 3 个 Row Action + 打开目录按钮
             │    └─ TabItem "日志"   → DataGrid<OperationLogEntry> (只读)
             └─ 底部: "打开备份目录" 按钮 + "关闭" 按钮

Services
  IBackupService  ──→ BackupService
    .Export(hive, subKey, scopeId, verbName) → .reg 路径  (已有)
    .List() → IReadOnlyList<BackupRecord>                    (新)
    .Import(path) → void                                      (新)
    .Delete(path) → void                                      (新)
  IOperationLog   ──→ OperationLogService
    .Append(entry)                                            (已有)
    .ReadAll() → IReadOnlyList<OperationLogEntry>             (新)

Models
  BackupRecord                                                     (新)
    FilePath, Timestamp, ScopeId, VerbName, Operation,
    RegistryPath (hive\subkey), Success?, Error?
    static FromFile(path, log) → BackupRecord?
    static ReadDirectory(dir, log) → IReadOnlyList<BackupRecord>
```

## 4. 组件

### 4.1 `Models/BackupRecord.cs`（新）

不可变 record：

```csharp
public sealed record BackupRecord(
    string FilePath,
    DateTime Timestamp,
    string ScopeId,
    string VerbName,
    string Operation,
    string? RegistryPath,
    bool? Success,
    string? Error);
```

静态方法：
- `static BackupRecord? FromFile(string filePath, IReadOnlyList<OperationLogEntry> log)`：解析文件名，命中 log 中 `backupPath` 字段时填充 Operation / RegistryPath / Success / Error。无法解析（不是 .reg 或时间戳格式错）返回 `null`。
- `static IReadOnlyList<BackupRecord> ReadDirectory(string dir, IReadOnlyList<OperationLogEntry> log)`：列出目录所有 `*.reg`，逐个 `FromFile`，按 Timestamp 倒序返回（最新在前）。

文件名约定由 `BackupService.Export` 决定：`yyyyMMdd-HHmmss-{scope}-{verb}.reg`（scope 可空，双连字符；verb 末尾是 `.reg`）。解析规则：
- 剥离 `.reg` 后取前 15 字符 `yyyyMMdd-HHmmss`（验证 `stem[8] == '-'`、`DateTime.TryParseExact`）。
- 余下部分按最后一个 `-` 切分，前段是 scope（可能为空），后段是 verb。

### 4.2 `Services/IBackupService.cs`（改）

新增：
```csharp
IReadOnlyList<BackupRecord> List();
void Import(string filePath);
void Delete(string filePath);
```

`List()` 内部委托给 `BackupRecord.ReadDirectory(_backupDir, log)` —— 但 `IBackupService` 不依赖 `IOperationLog`，所以参数不传；默认 log 为空集合（即 Operation / Success / Error 全为 null / Unknown）。这意味着 `BackupDialogViewModel` 拿到 `IBackupService` 后会自己注入 `IOperationLog.ReadAll()` 二次刷新。

实际数据流：`BackupDialogViewModel` 构造时拿 `IBackupService` 与 `IOperationLog`，先调 `backup.List()` 拿原始 BackupRecord，再调 `log.ReadAll()` 拿所有日志，然后**对每条 BackupRecord 重新交叉 log**（基于 `FilePath`）填字段。这样保证 `List()` 接口纯粹不依赖日志。

具体实现方式：在 `BackupDialogViewModel` 里维护一个 `Dictionary<string, OperationLogEntry>`，从 log 构建，对每条 record 查表填充。

### 4.3 `Services/BackupService.cs`（改 + 修复 mojibake）

新增方法：

```csharp
public IReadOnlyList<BackupRecord> List()
{
    if (!Directory.Exists(_backupDir)) return Array.Empty<BackupRecord>();
    var files = Directory.EnumerateFiles(_backupDir, "*.reg");
    var records = new List<BackupRecord>();
    foreach (var f in files)
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
        FileName = "reg.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
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
    if (!File.Exists(filePath)) return;
    File.Delete(filePath);
}
```

顺手把现有 `Export` 里的中文乱码 `无法启动 reg.exe` / `reg.exe export 失败 (exit=...): {err}` / `目标键不存在：{hive}\{subKey}` 改成正确 UTF-8 中文（之前会话写崩了）。

### 4.4 `Services/IOperationLog.cs` + `OperationLogService.cs`（改）

接口加：
```csharp
IReadOnlyList<OperationLogEntry> ReadAll();
```

实现：
```csharp
public IReadOnlyList<OperationLogEntry> ReadAll()
{
    if (!File.Exists(_path)) return Array.Empty<OperationLogEntry>();
    var list = new List<OperationLogEntry>();
    foreach (var line in File.ReadAllLines(_path))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        try { list.Add(JsonSerializer.Deserialize<OperationLogEntry>(line)!); }
        catch { /* 跳过损坏行 */ }
    }
    return list;
}
```

### 4.5 `ViewModels/BackupDialogViewModel.cs`（新）

```csharp
public partial class BackupDialogViewModel : ObservableObject
{
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;
    private readonly Action _onRestored;  // 通知主窗口刷新当前作用域

    public ObservableCollection<BackupRecord> Backups { get; } = new();
    public ObservableCollection<OperationLogEntry> Operations { get; } = new();

    [ObservableProperty] private BackupRecord? _selectedBackup;
    [ObservableProperty] private OperationLogEntry? _selectedOperation;
    [ObservableProperty] private string _statusText = "就绪";

    public string BackupDir { get; }   // IBackupService 暴露一个属性更省事；或从 ctor 传入
    public bool HasSelection => SelectedBackup is not null;

    public BackupDialogViewModel(IBackupService backup, IOperationLog log, Action onRestored, string backupDir)
    {
        _backup = backup;
        _log = log;
        _onRestored = onRestored;
        BackupDir = backupDir;
        Refresh();
    }

    public void Refresh()
    {
        Backups.Clear();
        var logEntries = _log.ReadAll();
        var logByPath = logEntries
            .Where(e => !string.IsNullOrEmpty(e.backupPath))
            .ToDictionary(e => e.backupPath!);
        foreach (var rec in _backup.List())
        {
            if (logByPath.TryGetValue(rec.FilePath, out var e))
            {
                rec = rec with
                {
                    Operation = e.op,
                    RegistryPath = $"{e.hive}\\{e.subKey}",
                    Success = e.success,
                    Error = e.error,
                };
            }
            Backups.Add(rec);
        }
        Operations.Clear();
        foreach (var e in logEntries.AsEnumerable().Reverse())
            Operations.Add(e);
        StatusText = $"共 {Backups.Count} 条备份 / {Operations.Count} 条日志";
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void Restore(BackupRecord? rec)
    {
        if (rec is null) return;
        // 调用方负责 ConfirmDialog + 真正执行；这里只触发回调
        _onRestored(rec);
    }

    [RelayCommand]
    private void DeleteBackup(BackupRecord? rec)
    {
        if (rec is null) return;
        _backup.Delete(rec.FilePath);
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder() => Process.Start(new ProcessStartInfo { FileName = BackupDir, UseShellExecute = true });

    partial void OnSelectedBackupChanged(BackupRecord? value) => OnPropertyChanged(nameof(HasSelection));
}
```

> 还原的实际执行放在 `MainWindow` 一侧（因为需要 UAC 弹窗 + 刷当前作用域）。`BackupDialogViewModel.Restore` 只回调。

### 4.6 `Views/Dialogs/BackupDialog.xaml` + code-behind（新）

布局：
- `Window` 标题"备份与日志"，`Width="900" Height="560"`，`SizeToContent` 不开（固定大小）。
- 顶部 `TabControl`：
  - `TabItem Header="备份"`：内放 `DataGrid`（`AutoGenerateColumns="False"`），列：
    - 时间（Timestamp，`StringFormat="yyyy-MM-dd HH:mm:ss"`）
    - 作用域（ScopeId）
    - Verb 名
    - 操作（Operation）
    - 状态（Success 转 "成功" / "失败" / "未知"）
    - 错误（Error，截断）
  - `TabItem Header="日志"`：同样 DataGrid，列：时间、动作、作用域、Verb、Hive/SubKey、状态、错误。
- 底部状态栏：`StatusText` + 两个按钮"打开备份目录" / "关闭"。

每行 DataGrid 旁加一列操作按钮（用 `DataGridTemplateColumn` + `StackPanel`）：
- 备份 Tab：还原（仅 `Success == true` 时可点）、删除、打开所在文件夹。
- 日志 Tab：无行内动作。

DataGrid 通过 `ItemsSource` 绑 `Backups` / `Operations`。

### 4.7 `ViewModels/MainViewModel.cs`（改）

加：
```csharp
public IBackupService Backup { get; }
public IOperationLog Log { get; }

public MainViewModel(..., IBackupService backup, IOperationLog log)
{
    ...
    Backup = backup;
    Log = log;
}

[RelayCommand]
private void ShowBackups()
{
    var owner = Application.Current?.MainWindow;
    var vm = new BackupDialogViewModel(Backup, Log, OnBackupRestored, BackupService.DefaultBackupDir());
    var dlg = new BackupDialog { Owner = owner, DataContext = vm };
    dlg.ShowDialog();
}

private void OnBackupRestored(BackupRecord rec)
{
    // 关闭弹窗
    if (Application.Current?.Windows.OfType<BackupDialog>().FirstOrDefault() is { } dlg)
        dlg.Close();
    if (!ConfirmDialog.Show("恢复备份", $"将导入 {rec.FilePath}，覆盖当前注册表项。继续？", "恢复", isDestructive: true))
        return;
    if (!EnsureAdministratorAsync(/* 需要从 rec 推 hive / subKey */, ...).Result) return;
    try
    {
        Backup.Import(rec.FilePath);
        StatusText = $"已恢复 {rec.VerbName}";
        _ = RefreshAsync();
    }
    catch (Exception ex)
    {
        StatusText = $"恢复失败: {ex.Message}";
    }
}
```

> `OnBackupRestored` 拿不到原 `hive/subKey` 信息（BackupRecord 已包含但还要拼路径）。简化：从 `rec.RegistryPath`（形如 `HKCU\Software\Classes\Directory\shell\xxx`）解析出 hive 与 subKey。

### 4.8 `Views/Controls/ScopeBar.xaml`（改）

在"刷新"按钮之前加一个：

```xml
<Button Grid.Column="5" Content="备份" Margin="6,0,0,0" Padding="12,4"
        Command="{Binding ShowBackupsCommand}" />
```

列号向后挪一格（原来是 `*` 的第 5 列变成 6，第 6 变成 7），或者插入到 "刷新" 之前。

### 4.9 `App.xaml.cs`（不改）

DI 注册已经齐全：`IBackupService` / `IOperationLog` 已注册；`MainViewModel` 构造需要新参数，DI 容器自动注入。

## 5. 数据流

1. 用户点击 ScopeBar 的"备份"按钮 → `MainViewModel.ShowBackupsCommand` 触发 → `new BackupDialog()` → 构造 `BackupDialogViewModel` 读 `Backup.List()` + `Log.ReadAll()` → 弹窗显示。
2. 用户在备份 Tab 选一行 → `SelectedBackup` 变化 → 还原/删除按钮的可点状态由 `HasSelection` 控制。
3. 用户点"还原" → `RestoreCommand(rec)` → 回调 `OnBackupRestored(rec)` → 弹窗关闭 → `ConfirmDialog` → `Backup.Import(rec.FilePath)` → 调 `RefreshAsync` 刷新当前作用域。
4. 用户点"删除" → `DeleteBackupCommand(rec)` → `Backup.Delete(rec.FilePath)` → `Refresh()` 重新加载。
5. 用户点"打开备份目录" → `OpenFolderCommand` → `Process.Start(explorer.exe, BackupDir)`。

## 6. UI 行为

| 场景 | 行为 |
|---|---|
| 启动弹窗 | 异步（虽然是同步调用，但 reg 目录一般 <100 个文件，秒级） |
| 备份目录不存在 | 显示"共 0 条备份" |
| 日志为空 | 日志 Tab 显示"共 0 条日志" |
| 选备份项 | "还原" / "删除" 按钮可点 |
| 还原确认 | 关闭弹窗 → 弹 `ConfirmDialog`（危险） → 用户确认 → 真正执行 |
| 失败项 | "还原" 按钮禁用（仅 `Success == true` 可还原） |
| 双 Tab 切换 | 数据各自保持 |
| 关闭弹窗 | 直接 `Close()`，无副作用 |

## 7. 错误处理

| 异常 | 处理 |
|---|---|
| `BackupRecord.FromFile` 解析失败（非 .reg / 错文件名） | 跳过该文件，不进列表 |
| `Log.ReadAll` 遇到坏 JSON 行 | 跳过该行 |
| `reg.exe import` 失败 | 抛异常 → `OnBackupRestored` 捕获 → 状态栏显示错误 |
| `Backup.Delete` 文件不存在 | 静默 no-op |
| `BackupDir` 不存在 | `List()` 返回空，弹窗显示空状态 |

## 8. 测试

新增单元测试（`Tests/BackupServiceTests.cs` + `Tests/BackupRecordTests.cs`）：

- `BackupRecord.FromFile` 解析正常 `20260617-142530-Folder-OpenWith.reg` → Timestamp / ScopeId=Folder / VerbName=OpenWith
- `BackupRecord.FromFile` 解析空 scope（`20260617-142530--OpenWith.reg`） → ScopeId="" / VerbName=OpenWith
- `BackupRecord.FromFile` 拒绝非 .reg / 错时间戳 / 太短文件名
- `BackupService.List` 在临时目录创建 3 个 .reg + 1 个 .txt → 列表含 3 条记录，按 timestamp 倒序
- `BackupService.Delete` 删存在文件 → 文件消失；删不存在文件 → 无异常
- `OperationLogService.ReadAll` 写入 2 行 JSON + 1 行损坏 → 返回 2 条

不测 `Import`（需要真实 reg.exe + 注册表写权限，环境敏感）。

## 9. 风险

| 风险 | 缓解 |
|---|---|
| `reg.exe import` 覆盖当前数据，可能破坏系统 | `ConfirmDialog` 二次确认 + `isDestructive=true`；按钮文案"恢复"配警告色 |
| 备份文件被外部删除 | `List()` 实时读目录，删除的项不出现 |
| 备份文件特别多（>1000） | 暂不优化；后续可加分页 / 折叠 |
| `BackupRecord.RegistryPath` 解析 `hive\subKey` 字符串时 hive 名字不匹配 | 用 `BackupService.HiveDisplayName` 统一 |
| `BackupService.Export` 里的旧 mojibake 修了但破坏 ABI | 只改字面量中文字符串，不改签名 |
| `OnBackupRestored` 内部 `EnsureAdministratorAsync(...).Result` 死锁 | 用 `await` 替代，但需要把 `RestoreCommand` 改成 `async Task` |
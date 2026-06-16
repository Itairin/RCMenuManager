# Windows 右键菜单注册表参考

## 作用域 Registry 路径

| 作用域 | Registry 路径 |
|--------|---------------|
| 文件 (*.*) | HKCR\*\shell |
| 所有文件系统对象 | HKCR\AllFilesystemObjects\shell |
| 文件夹 | HKCR\Directory\shell |
| 文件夹背景 | HKCR\Directory\Background\shell |
| 驱动器 | HKCR\Drive\shell |
| 桌面 | HKCR\DesktopBackground\Shell |
| 特定文件类型 | HKCR\<扩展名>\shell |

## 用户级 Registry 路径

用户级路径（HKCU）可以覆盖系统级路径（HKCR/HKLM），且不需要管理员权限：

| 作用域 | Registry 路径 |
|--------|---------------|
| 文件 | HKCU\Software\Classes\*\shell |
| 文件夹 | HKCU\Software\Classes\Directory\shell |
| 文件夹背景 | HKCU\Software\Classes\Directory\Background\shell |

## 菜单项 Registry 结构

### 基本结构

```
HKEY_CLASSES_ROOT\<scope>\shell\<VerbName>
    (Default) = "显示名称"
    Icon = "图标路径"
    Position = "Top" | "Bottom"
    Extended = "" (仅 Shift+右键显示)
    ProgrammaticAccessOnly = "" (仅程序调用)
    SuppressionPolicy = DWORD (隐藏策略)

HKEY_CLASSES_ROOT\<scope>\shell\<VerbName>\command
    (Default) = "执行命令"
```

### 级联菜单（二级菜单）

```
HKEY_CLASSES_ROOT\<scope>\shell\ParentVerb
    (Default) = "父菜单名称"
    SubCommands = REG_MULTI_SZ
        ChildVerb1
        ChildVerb2

HKEY_CLASSES_ROOT\<scope>\shell\ParentVerb\shell\ChildVerb1
    (Default) = "子菜单1名称"
    command
        (Default) = "命令1"

HKEY_CLASSES_ROOT\<scope>\shell\ParentVerb\shell\ChildVerb2
    (Default) = "子菜单2名称"
    command
        (Default) = "命令2"
```

## 常用 Registry 值

| 值名称 | 类型 | 说明 |
|--------|------|------|
| (Default) | REG_SZ | 菜单项显示名称 |
| Icon | REG_SZ | 图标路径（DLL/EXE, -资源ID） |
| Position | REG_SZ | 位置：Top/Bottom/默认 |
| Extended | REG_SZ | 空值，仅 Shift+右键显示 |
| ProgrammaticAccessOnly | REG_SZ | 空值，仅程序调用 |
| NeverDefault | REG_SZ | 空值，不作为默认动作 |
| AppliesTo | REG_SZ | AQS 过滤条件 |
| DeploymentImplication | DWORD | 部署策略 |
| SuppressionPolicy | DWORD | 隐藏策略 |

## Win11 新菜单控制

### 禁用 Win11 新菜单（恢复经典菜单）

```
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
    (Default) = "" (空字符串)
```

### 启用 Win11 新菜单（默认）

删除上述键值：

```powershell
Remove-Item -Path "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" -Recurse
```

### Win11 新菜单 Block 列表

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked
    {CLSID} = "" (阻塞该扩展)
```

## Shell 扩展注册

### Shell Extension Handler

```
HKEY_CLASSES_ROOT\<ProgID>\ShellEx\ContextMenuHandlers\<HandlerName>
    (Default) = "{CLSID}"

HKEY_CLASSES_ROOT\CLSID\{CLSID}
    (Default) = "Handler Description"

HKEY_CLASSES_ROOT\CLSID\{CLSID}\InprocServer32
    (Default) = "path\to\handler.dll"
    ThreadingModel = "Apartment"
```

## 常用 ProgID

| ProgID | 说明 |
|--------|------|
| * | 所有文件 |
| Directory | 文件夹 |
| Directory\Background | 文件夹背景 |
| Drive | 驱动器 |
| DesktopBackground | 桌面 |
| AllFilesystemObjects | 所有文件系统对象 |

## Registry 操作示例

### 读取菜单项

```csharp
using Microsoft.Win32;

var key = Registry.ClassesRoot.OpenSubKey(@"*\shell");
if (key != null)
{
    foreach (var subKeyName in key.GetSubKeyNames())
    {
        using var subKey = key.OpenSubKey(subKeyName);
        var name = subKey.GetValue("(Default)") as string;
        var icon = subKey.GetValue("Icon") as string;
        
        using var cmdKey = key.OpenSubKey($@"{subKeyName}\command");
        var command = cmdKey?.GetValue("(Default)") as string;
    }
}
```

### 添加菜单项

```csharp
using Microsoft.Win32;

using var key = Registry.ClassesRoot.CreateSubKey(@"*\shell\MyVerb");
key.SetValue("", "我的菜单项");
key.SetValue("Icon", "imageres.dll,-64");

using var cmdKey = Registry.ClassesRoot.CreateSubKey(@"*\shell\MyVerb\command");
cmdKey.SetValue("", "notepad.exe \"%1\"");
```

### 删除菜单项

```csharp
using Microsoft.Win32;

Registry.ClassesRoot.DeleteSubKeyTree(@"*\shell\MyVerb");
```

### 通知 Shell 刷新

```csharp
[DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

const int SHCNE_ASSOCCHANGED = 0x08000000;
const int SHCNF_IDLIST = 0;

SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
```

## 系统关键菜单项

以下菜单项是 Windows 系统关键项，删除前需要二次确认：

| VerbName | 说明 |
|----------|------|
| open | 打开 |
| edit | 编辑 |
| print | 打印 |
| explore | 资源管理器 |
| find | 搜索 |
| opennewwindow | 在新窗口中打开 |
| opennewprocess | 在新进程中打开 |
| copyaspath | 复制为路径 |
| printto | 打印到 |

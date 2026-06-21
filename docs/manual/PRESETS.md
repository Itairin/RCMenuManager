# 推荐设置 - 预设配置清单

## 文件类推荐设置（作用域: 文件）

| VerbName | DisplayName | Command | Icon | Extended | 说明 |
|----------|-------------|---------|------|----------|------|
| notepad | 用记事本打开 | notepad.exe "%1" | imageres.dll,-64 | false | 快速编辑文本文件 |
| vscode | 用 VS Code 打开 | code "%1" | code.exe,0 | false | 开发者必备 |
| copypath | 复制文件路径 | cmd /c echo "%1" \| clip | imageres.dll,-5302 | false | 复制完整路径到剪贴板 |
| copyname | 复制文件名 | cmd /c echo "%~nx1" \| clip | imageres.dll,-5302 | false | 仅复制文件名 |
| openhere | 在终端中打开 | pwsh -NoExit -Command "cd '%~dp1'" | powershell.exe,0 | false | PowerShell 终端 |
| hash | 文件哈希校验 | certutil -hashfile "%1" SHA256 | imageres.dll,-67 | false | 计算文件哈希 |
| runas | 以管理员身份运行 | "runas.exe" /user:Administrator "%1" | imageres.dll,-78 | true | 提权运行（仅 Shift 显示） |
| openwith | 打开方式... | rundll32.exe shell32.dll,OpenAs_RunDLL "%1" | imageres.dll,-5301 | false | 选择打开程序 |

## 文件夹类推荐设置（作用域: 文件夹）

| VerbName | DisplayName | Command | Icon | Extended | 说明 |
|----------|-------------|---------|------|----------|------|
| vscode | 在 VS Code 中打开 | code "%V" | code.exe,0 | false | VS Code 打开目录 |
| openhere | 在终端中打开 | pwsh -NoExit -Command "cd '%V'" | powershell.exe,0 | false | PowerShell 终端 |
| gitbash | 在 Git Bash 中打开 | "C:\Program Files\Git\git-bash.exe" --cd="%V" | bash.exe,0 | false | Git 终端 |
| newfile | 新建文件 | cmd /c echo. > "%V\新建文件.txt" | imageres.dll,-64 | false | 快速创建空文件 |
| folderstats | 文件夹大小统计 | powershell -Command "Get-ChildItem -Recurse '%V' \| Measure-Object -Property Length -Sum" | imageres.dll,-67 | true | 计算文件夹大小 |
| copyfolderpath | 复制文件夹路径 | cmd /c echo "%V" \| clip | imageres.dll,-5302 | false | 复制完整路径 |

## 文件夹背景类推荐设置（作用域: 文件夹背景）

| VerbName | DisplayName | Command | Icon | Extended | 说明 |
|----------|-------------|---------|------|----------|------|
| openhere | 在终端中打开 | pwsh -NoExit -Command "cd '%V'" | powershell.exe,0 | false | 当前目录打开终端 |
| vscode | 在 VS Code 中打开 | code "%V" | code.exe,0 | false | VS Code 打开 |
| newtxt | 新建文本文档 | cmd /c echo. > "%V\新建文本文档.txt" | imageres.dll,-64 | false | 快速创建 |
| showhidden | 显示隐藏文件 | reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v Hidden /t REG_DWORD /d 1 /f | imageres.dll,-527 | true | 切换显示隐藏 |
| hidehidden | 隐藏文件 | reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v Hidden /t REG_DWORD /d 2 /f | imageres.dll,-527 | true | 切换隐藏文件 |
| refreshicons | 刷新图标缓存 | ie4uinit.exe -show | imageres.dll,-5305 | false | 刷新图标 |

## 桌面类推荐设置（作用域: 桌面）

| VerbName | DisplayName | Command | Icon | Extended | 说明 |
|----------|-------------|---------|------|----------|------|
| settings | 打开系统设置 | ms-settings: | imageres.dll,-121 | false | 快速打开设置 |
| taskmgr | 打开任务管理器 | taskmgr.exe | imageres.dll,-5305 | false | 快速打开任务管理器 |
| regedit | 打开注册表编辑器 | regedit.exe | imageres.dll,-5007 | false | 打开注册表 |
| devmgr | 打开设备管理器 | devmgmt.msc | imageres.dll,-27 | false | 打开设备管理器 |
| control | 打开控制面板 | control.exe | imageres.dll,-5002 | false | 打开控制面板 |
| cmd | 在此处打开命令行 | cmd.exe | cmd.exe,0 | false | 打开 CMD |
| powershell | 在此处打开 PowerShell | pwsh.exe | powershell.exe,0 | false | 打开 PowerShell |

## 驱动器类推荐设置（作用域: 驱动器）

| VerbName | DisplayName | Command | Icon | Extended | 说明 |
|----------|-------------|---------|------|----------|------|
| openhere | 在终端中打开 | pwsh -NoExit -Command "cd '%V'" | powershell.exe,0 | false | PowerShell 终端 |
| open | 打开 | explorer.exe "%V" | shell32.dll,-16777 | false | 打开驱动器 |
| diskmgmt | 磁盘管理 | diskmgmt.msc | imageres.dll,-5305 | false | 打开磁盘管理 |
| properties | 属性 | rundll32.exe shell32.dll,Properties_RunDLL "%V" | imageres.dll,-5306 | false | 查看属性 |

## 配置文件格式（JSON）

```json
{
  "version": "1.0",
  "presets": [
    {
      "scope": "File",
      "verbName": "notepad",
      "displayName": "用记事本打开",
      "command": "notepad.exe \"%1\"",
      "icon": "imageres.dll,-64",
      "extended": false,
      "description": "快速编辑文本文件",
      "isSystem": false
    }
  ]
}
```

## 注册表写入示例

```
用记事本打开:
HKEY_CLASSES_ROOT\*\shell\notepad
    (Default) = "用记事本打开"
    Icon = "imageres.dll,-64"
HKEY_CLASSES_ROOT\*\shell\notepad\command
    (Default) = "notepad.exe \"%1\""

用 VS Code 打开（仅 Shift 显示）:
HKEY_CLASSES_ROOT\*\shell\vscode
    (Default) = "用 VS Code 打开"
    Icon = "code.exe,0"
    Extended = ""
HKEY_CLASSES_ROOT\*\shell\vscode\command
    (Default) = "code \"%1\""
```

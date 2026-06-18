# Phase 7 手工冒烟 (推荐设置)

## 前置
- 已 `dotnet build` 成功
- 启动 RCMenuManager (推荐以管理员身份运行, 但非必须 — 全程 HKCU)

## 验证步骤

1. 打开对话框: 点 ScopeBar 上的 "推荐" 按钮, 对话框应弹出, 5 个分组 (文件 / 文件夹 / 文件夹背景 / 驱动器 / 桌面) 全部展开, 每组下列出对应预设.
2. 状态文本: 底部状态栏应显示 "共 N 项预设" (N ≈ 31).
3. 未应用: 之前没装过的预设, 右侧不显示 "已应用" 徽章.
4. 勾选 + 应用: 勾选 "文件 / 用记事本打开" 和 "文件 / 复制文件路径" 两项, 点 "应用选中". 状态应变成 "应用 2 · 跳过 0 · 失败 0", 两条右侧出现 "已应用" 徽章.
5. 注册表: 打开 `regedit`, 确认:
   - `HKCU\Software\Classes\*\shell\notepad\(Default)` = "用记事本打开"
   - `HKCU\Software\Classes\*\shell\notepad\Icon` = "imageres.dll,-64"
   - `HKCU\Software\Classes\*\shell\notepad\command\(Default)` = `notepad.exe "%1"`
   - `HKCU\Software\Classes\*\shell\copypath\command\(Default)` = `cmd /c echo "%1" | clip`
6. 真实菜单: 在资源管理器中右键任意 .txt, 应出现 "用记事本打开" 项. (部分场景需要 `ie4uinit.exe -show` 刷新图标缓存.)
7. 冲突处理: 不勾选 "覆盖已存在的 verb", 再次勾选 "用记事本打开" + 应用. 状态徽章变 "已存在", 状态栏 +1 跳过, 原 verb 不被修改.
8. 覆盖处理: 勾选 "覆盖已存在的 verb", 再点 "应用选中". 原 verb 被删 + 重建, 备份 .reg 出现在 `%LocalAppData%\RCMenuManager\backups\`.
9. Shift 预设: 应用 "文件 / 以管理员身份运行" (extended=true). 注册表应有 `Extended = ""` 空值. 资源管理器中 Shift+右键应看到该项.
10. 导入: 准备一个简单 JSON 文件, 用 "导入" 按钮选它. 状态栏显示 "已导入: <path>".
11. 导出: 点 "导出", 选个路径, 状态栏显示 "已导出". 打开文件, 确认包含内置预设.
12. 删除清理: 在注册表中手动删除测试创建的 verb, 回到对话框, 点 "刷新", "已应用" 徽章消失.
13. 重启验证: 关闭再打开 RCMenuManager, 推荐对话框再次打开, IsApplied 状态应保持正确.

## 清理

```powershell
Remove-Item -Path "HKCU:\Software\Classes\*\shell\notepad" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Classes\*\shell\copypath" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Classes\*\shell\runas" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "$env:LOCALAPPDATA\RCMenuManager\presets.json" -ErrorAction SilentlyContinue
```

# Phase 3 手动 Smoke 测试

所有步骤都在 HKCU 下进行，不影响 HKLM。如果中途异常，可参考 `%LOCALAPPDATA%\RCMenuManager\backups` 与 `operations.log` 还原。

## 1. 新增一级菜单
1. 启动 RCMenuManager（asInvoker）。
2. 选择"文件夹 (HKCR\Directory\shell)"。
3. 点击右上角"+ 新增菜单项"。
4. 输入 verb=`smoke-test`，显示名=`Smoke Test`，命令=`cmd.exe /c echo smoke && pause`。
5. 点击确定 -> 状态栏显示"已新增 smoke-test"。
6. 验证：右键点击任一文件夹，应看到"Smoke Test"。

## 2. 编辑命令
1. 选中刚才的 `smoke-test` 项。
2. 点击"编辑"。
3. 命令改为 `notepad.exe`。
4. 点击保存 -> 状态栏显示"已保存 smoke-test"。
5. 验证：右键再点 Smoke Test 应启动 Notepad。

## 3. 禁用 / 启用
1. 选中 smoke-test。
2. 点击"启用/禁用" -> 树视图标记刷新为"仅程序"，状态栏显示"已切换"。
3. 验证：右键文件夹时不再出现 Smoke Test。
4. 再次点击"启用/禁用" -> 标记消失。

## 4. 新增二级菜单
1. 选中 smoke-test。
2. 点击"新增子项"。
3. 输入 verb=`child-a`，显示名=`Child A`，命令=`calc.exe`。
4. 验证：右键文件夹 -> Smoke Test 现在弹出二级菜单，含 Child A。

## 5. 删除
1. 选中 smoke-test。
2. 点击"删除"，在确认框选"删除"。
3. 验证：smoke-test 从列表消失，注册表中 `HKCU\Software\Classes\Directory\shell\smoke-test` 不存在。

## 6. 备份与日志
1. 打开 `%LOCALAPPDATA%\RCMenuManager\backups`，确认存在多份 .reg 文件，每份对应一次写入。
2. 打开 `%LOCALAPPDATA%\RCMenuManager\operations.log`，确认每次操作有一行 JSON，含 success=true。

## 7. HKLM 提权
1. 选择"驱动器 (HKCR\Drive\shell)" 之类的、当前账号没有 HKCU 子键的作用域。
2. 选中一个 HKLM 来源的项 -> 点击"启用/禁用"。
3. 期望：弹出"需要管理员权限"对话框；点"重启"后程序退出，新进程以管理员身份恢复同一作用域。
4. 取消该步骤。注意：不在 smoke 中真写 HKLM，避免污染机器配置。

# Phase 8: 拖拽识别 手动 Smoke 检查

> 配套实现: docs/superpowers/plans/2026-06-17-phase8-dragdrop.md  
> 配套设计: docs/superpowers/specs/2026-06-17-phase8-dragdrop-design.md  
> 执行人: 手动  
> 环境: Windows 10 / Windows 11, RCMenuManager 以管理员身份运行

## [前置]
- [ ] 重新构建并以管理员身份启动 RCMenuManager
- [ ] 状态栏显示“就绪”，左侧作用域栏显示 6 个内置作用域
- [ ] 默认作用域是“文件 (所有文件)”，列表已加载

## [拖入文件]
- [ ] 从资源管理器拖一个 .txt 文件进 RCMenuManager 窗口
- [ ] 拖动时主窗口显示蓝色 overlay + 居中提示文字“拖入文件 / 文件夹 / 驱动器以自动识别作用域”
- [ ] 释放后作用域切到 `.txt 文件`（或对应 ProgID），列表自动加载 .txt 的右键菜单项
- [ ] 状态栏反馈“已切换到 .txt 文件”
- [ ] 再拖另一个 .txt 文件进窗口：作用域已存在，SelectedScope 复用同一项，列表重新加载
- [ ] 拖一个 .cs / .png / .exe 等其他扩展名：作用域切到对应扩展名，状态栏显示扩展名标签

## [拖入文件夹]
- [ ] 拖任意一个文件夹进窗口
- [ ] 释放后作用域切到“文件夹 (HKCR\Directory\shell)”，列表加载
- [ ] 状态栏反馈“已切换到文件夹 <path>”

## [拖入驱动器]
- [ ] 拖驱动器根（C:\、D:\ 等）进窗口
- [ ] 释放后作用域切到“驱动器 (HKCR\Drive\shell)”，列表加载
- [ ] 状态栏反馈“已切换到驱动器 <path>”

## [多文件]
- [ ] 同时选中 .png + .jpg 拖入
- [ ] 释放后作用域切到第一个文件（.png），状态栏显示 .png 标签
- [ ] 列表加载 .png 的菜单项（不是 .jpg）

## [无扩展名]
- [ ] 新建一个 `README`（无扩展名）拖入
- [ ] 释放后作用域切到 `文件 (所有文件)`（即 *），状态栏显示“无扩展名，已切换到通用文件”
- [ ] 列表加载 HKCR\*\shell 下的菜单项

## [拖动中移开]
- [ ] 拖到窗口上 → overlay 显示
- [ ] 拖回资源管理器（移开窗口）→ overlay 消失
- [ ] 拖到非客户区（标题栏）→ overlay 也不显示（Windows 默认不接受标题栏的 drop）

## [拖非文件]
- [ ] 从浏览器拖一段文字或 URL 到窗口
- [ ] overlay 不显示（DataFormats.FileDrop 不匹配），窗口不接受
- [ ] 从聊天软件拖一张图片 → 同上

## [拖入不存在路径]
- [ ] 用 PowerShell 模拟：在 `App.xaml.cs` 启动时直接调用 `vm.OnFileDroppedAsync(new[] { @"Z:\nope\missing.bin" })`
- [ ] 期望：状态栏显示“不支持的拖入内容：Z:\nope\missing.bin”，作用域不变
- [ ] 或：临时把 `File.Exists` / `Directory.Exists` 之前的逻辑里塞一个不存在的路径进 FileTypeService.Identify → 期待返回 `Unknown`

## [管理员 + UAC]
- [ ] 不以管理员身份运行 RCMenuManager（绕过 manifest 测试）：拖入任意项应仍能切作用域、加载列表（读操作不需要管理员）
- [ ] 以管理员身份运行（默认 manifest 行为）：拖入 .exe 之类的系统作用域，菜单项可正常禁用/删除

## [回归]
- [ ] 顶部“刷新”按钮、Win11 按钮、备份按钮依旧可用
- [ ] “自定义扩展名”输入框 → 加载按钮：与拖入文件的扩展名走相同的 `SwitchToExtensionScopeAsync` 路径
- [ ] 拖完文件后立刻按“刷新”，列表应与拖入后的状态一致

## [失败上报]
- [ ] 出现异常时，状态栏显示“切换失败：<message>”，主进程不崩溃
- [ ] `LocalAppData\RCMenuManager\crash.log` 没有新增未处理异常条目

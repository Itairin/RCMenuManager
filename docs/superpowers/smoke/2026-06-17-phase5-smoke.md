# Phase 5: Win11 新菜单 手动 Smoke 检查

> 仅在 Win11 系统上跑全部项；Win10 / 其他系统只跑 [通用]。

## [通用]
- [ ] 应用启动不报错
- [ ] ScopeBar 上"Win11"按钮在非 Win11 上灰态但可见
- [ ] ScopeBar 上"Win11"按钮在 Win11 上可点

## [Win11 系统]
- [ ] 点"Win11" → 弹窗打开，ToggleButton 状态反映当前 IsNewMenuEnabled
- [ ] 拨 ToggleButton → 状态栏出现"已切换到 ... （需重启资源管理器）"
- [ ] 点"重启资源管理器" → 弹 ConfirmDialog → 确认 → 资源管理器重启
- [ ] 重启后右键菜单实际生效（点桌面右键验证）
- [ ] Block 列表显示 HKCU\Software\Microsoft\Windows\CurrentVersion\Shell\Block 下的子键
- [ ] 选一项 → 点"移除" → 列表刷新，状态栏："已移除 xxx"
- [ ] 移除后重启资源管理器，新菜单里对应 verb 重新出现

## [边界]
- [ ] HKCU\Software\...\Shell\Block 不存在时：弹窗显示"共 0 项 Block"
- [ ] ToggleButton 状态与注册表实际值一致
- [ ] 关闭弹窗后再次打开：数据保持最新（无 stale 缓存）
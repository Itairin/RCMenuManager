# 本轮产出（可直接查看）

- **时间**：2026-11-16
- **测试**：212 通过 / 0 失败；Debug 与 Release 均 0 警告 0 错误

## 怎么看

1. **图**：`docs/review/preview/deliverables/*.png`，每张图配同名 `.txt`，里面是这张图**实际渲染出来的文字**（含非白像素占比，用来证明不是空白图）。
2. **能跑的成品**：`publish/portable/RCMenuManager.exe`（65 MB 单文件，拷走双击即用，无需装 .NET）。
3. **改了哪些文件**：`git status` / `git diff`。

## 四张图对应四个改动点

| 图 | 看什么 | 对应需求 |
|---|---|---|
| `01-list-source-badges.png` | 桌面作用域列表：5 项全部带来源徽章；`.SpotlightLearnMore` 显示为「了解有关此图片的详细信息」而不是键名；三项标「名称推断」；`显示设置(D)` 不再带 `&` | 需求 1 + 需求 3 |
| `02-details-source-block.png` | 详情面板「来源」区块：系统处理器 + CLSID 说明 + 证据 | 需求 3 |
| `03-details-shell-extension.png` | 预览里选中「外壳扩展」后的只读视图：CLSID / DLL / 注册名 | 需求 2 |
| `04-preview-popup.png` | 从真实 shell 菜单渲染的预览，条目可点选 | 需求 2 |

## 本轮的额外修复（由图发现）

渲染出的文字暴露出一个真实缺陷：列表里显示 `显示设置(&D)`、`个性化(&R)`，而资源管理器显示的是 `显示设置(D)` —— shell 的加速键标记被当普通字符画了出来。新增 `Helpers/MenuTextHelper` 去掉标记但保留字母，列表与详情标题改用它（预览菜单仍保留标记，以便像 Explorer 一样画下划线）。

## 以后每次改完怎么出产物

```powershell
$env:USERPROFILE\.dotnet\dotnet.exe run --project tools\render-previews -c Debug
```

工具位于 `tools/render-previews`，用真实控件离屏渲染（不弹窗、不碰桌面、只读注册表），输出覆盖 `docs/review/preview/deliverables/`。改完代码后重跑一次，图与文字转储就是这次改动的结果。

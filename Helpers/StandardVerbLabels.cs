using System;
using System.Collections.Generic;

namespace RCMenuManager.Helpers;

/// <summary>
/// Localized text for the shell's own verbs. Measured against the menu Windows
/// builds on a zh-CN system: <c>HKCR\exefile\shell\open</c> carries no default
/// value and no <c>MUIVerb</c>, so a naive parser ends up listing the raw verb
/// names ("open", "runas") while Explorer shows 打开(O) / 以管理员身份运行(A).
/// </summary>
public static class StandardVerbLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = "打开(&O)",
        ["runas"] = "以管理员身份运行(&A)",
        ["runasuser"] = "以其他用户身份运行(&F)",
        ["opennew"] = "在新窗口中打开",
        ["edit"] = "编辑",
        ["print"] = "打印",
        ["printto"] = "打印",
        ["find"] = "查找",
        ["explore"] = "资源管理器",
        ["play"] = "播放",
        ["preview"] = "预览",
        ["properties"] = "属性",
    };

    /// <summary>Null when the verb is not one of the shell's well-known verbs.</summary>
    public static string? Resolve(string verbName)
        => !string.IsNullOrEmpty(verbName) && Labels.TryGetValue(verbName, out var label) ? label : null;

    /// <summary>
    /// Human-readable names for Win11 <c>ExplorerCommandHandler</c> CLSIDs whose
    /// registry entry carries only a technical name. Measured on a zh-CN Windows 11:
    /// <c>DesktopBackgroundShell.SpotlightLearnMore</c> registers the CLSID whose
    /// default value is literally ".SpotlightLearnMore", while Explorer draws a
    /// localized sentence for it. Without this table that raw identifier is the only
    /// thing the list could show.
    /// </summary>
    private static readonly Dictionary<string, string> HandlerLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{5C6B744C-4CD1-5EA7-BDC6-0E6679A956BF}"] = "了解有关此图片的详细信息",
        ["{2ECAB2B4-B6A8-5482-0AE6-D1A5BF594B00}"] = "切换到下一张图片",
        ["{3C0E1E28-51F1-4130-98AC-0FBFB5FE4638}"] = "编辑贴纸",
    };

    /// <summary>Null when the CLSID is not one of the handlers with a known label.</summary>
    public static string? ResolveHandler(string? clsid)
        => !string.IsNullOrEmpty(clsid) && HandlerLabels.TryGetValue(clsid, out var label) ? label : null;
}

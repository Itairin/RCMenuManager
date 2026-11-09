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
}

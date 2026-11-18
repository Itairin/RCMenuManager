using System;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.Tests;

/// <summary>
/// Some tests describe what the real shell menu looks like. That menu can only be built
/// on a machine with an interactive desktop and a populated shell; a CI runner has
/// neither, so those tests skip there instead of reporting a false regression.
/// </summary>
internal static class ShellEnvironment
{
    public const string SkipReason =
        "该环境无法构建真实 shell 菜单（无交互桌面或 shell 未初始化），跳过依赖它的断言";

    public const string SkipReasonChinese =
        "这些断言按 zh-CN 菜单文案校验（打开 / 属性(&R)），非中文界面跳过";

    private static readonly Lazy<bool> Available = new(Probe);

    public static bool CanBuildShellMenu => Available.Value;

    /// <summary>
    /// The expected labels below were measured on a zh-CN system ("打开", "属性(&R)").
    /// An English runner builds the same menu with different text, so the label
    /// assertions only make sense on a Chinese UI.
    /// </summary>
    public static bool CanAssertChineseLabels =>
        CanBuildShellMenu
        && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase);

    private static bool Probe()
    {
        try
        {
            var nodes = ShellContextMenuReader.Instance.TryRead(
                MenuScope.ForExtension(".txt", "txtfile"), extendedVerbs: false);
            return nodes is { Count: >= 3 };
        }
        catch
        {
            return false;
        }
    }
}

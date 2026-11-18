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

    private static readonly Lazy<bool> Available = new(Probe);

    public static bool CanBuildShellMenu => Available.Value;

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

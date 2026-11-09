using System.Collections.Generic;
using System.Windows.Media;

namespace RCMenuManager.Services;

/// <summary>
/// One row of the menu Windows actually produced. Unlike the hand-built
/// catalog this is the shell's own output: real labels (with accelerators),
/// real separators, real enabled/checked state and real icons.
/// </summary>
public sealed class ShellMenuNode
{
    public string Text { get; init; } = string.Empty;

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsDefault { get; init; }

    public bool IsChecked { get; init; }

    public ImageSource? Icon { get; init; }

    public IReadOnlyList<ShellMenuNode> Children { get; init; } = System.Array.Empty<ShellMenuNode>();

    public bool HasChildren => Children.Count > 0;

    public override string ToString() => IsSeparator ? "---" : Text;
}

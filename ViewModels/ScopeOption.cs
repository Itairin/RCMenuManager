using RCMenuManager.Models;

namespace RCMenuManager.ViewModels;

/// <summary>Display row in the scope dropdown. Bundles a label with the actual scope.</summary>
public sealed class ScopeOption
{
    public string Label { get; }
    public MenuScope Scope { get; }

    public ScopeOption(string label, MenuScope scope)
    {
        Label = label;
        Scope = scope;
    }

    public override string ToString() => Label;
}

using RCMenuManager.Models;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

public class PresetItemViewModelTests
{
    [Fact]
    public void ToDraft_copies_all_fields_from_model()
    {
        var model = new PresetItem
        {
            Scope = "AllFiles",
            VerbName = "vscode",
            DisplayName = "Open in VS Code",
            Command = "code %1",
            Icon = "code.exe,0",
            Extended = true,
            Position = "Top",
        };
        var vm = new PresetItemViewModel(model);
        var draft = vm.ToDraft();
        Assert.Equal("vscode", draft.VerbName);
        Assert.Equal("Open in VS Code", draft.DisplayName);
        Assert.Equal("code %1", draft.Command);
        Assert.Equal("code.exe,0", draft.IconExpression);
        Assert.True(draft.IsExtended);
        Assert.Equal("Top", draft.Position);
        Assert.False(draft.IsParentOnly);
    }

    [Fact]
    public void IsApplied_property_change_is_observable()
    {
        var vm = new PresetItemViewModel(new PresetItem { VerbName = "x" });
        var changed = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PresetItemViewModel.IsApplied)) changed = true; };
        vm.IsApplied = true;
        Assert.True(changed);
        Assert.True(vm.IsApplied);
    }
}

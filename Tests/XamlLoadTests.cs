using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using RCMenuManager.ViewModels;
using RCMenuManager.Views.Controls;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Guards the two things a successful build does not: that the XAML actually parses
/// at runtime (missing StaticResource keys only fail there) and that every binding
/// target still exists on the view model (binding paths are strings, so a rename
/// silently blanks the UI instead of breaking the build).
/// </summary>
public class XamlLoadTests
{
    [Fact]
    public void MenuTreeView_binding_targets_exist_on_the_view_model()
    {
        var expected = new[] { "SearchText", "ClearSearchCommand", "ListSummary", "FilteredItems" };
        var missing = expected
            .Where(name => typeof(MainViewModel).GetProperty(name) is null
                        && typeof(MainViewModel).GetProperty(name + "Command") is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void MenuItemViewModel_exposes_the_two_way_selection_flag()
    {
        var prop = typeof(MenuItemViewModel).GetProperty("IsSelected");
        Assert.NotNull(prop);
        Assert.True(prop!.CanWrite, "IsSelected must be settable: the TreeView binds it two-way");
    }

    [Fact]
    public void MenuTreeView_parses_with_the_application_resources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                var view = new MenuTreeView();
                Assert.NotNull(view.FindName("SearchBox"));
                Assert.NotNull(view.FindName("Tree"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("MenuTreeView failed to load: " + failure.Message, failure);
    }

    [Fact]
    public void AddVerbDialog_defaults_to_the_user_hive_and_hides_the_choice()
    {
        RunOnSta(() =>
        {
            var dlg = new Views.Dialogs.AddVerbDialog(new Services.IconService());
            // Child dialogs are bound to a parent that already sits in a specific hive,
            // so offering a scope switch there would be a lie.
            Assert.False(dlg.AllowMachineScope);
            Assert.Equal(Microsoft.Win32.RegistryHive.CurrentUser, dlg.TargetHive);
        });
    }

    [Fact]
    public void AddVerbDialog_offers_machine_scope_when_the_caller_allows_it()
    {
        RunOnSta(() =>
        {
            var dlg = new Views.Dialogs.AddVerbDialog(new Services.IconService()) { AllowMachineScope = true };
            Assert.True(dlg.AllowMachineScope);
            // Default stays "this account only" — machine scope is opt-in, never a surprise.
            Assert.Equal(Microsoft.Win32.RegistryHive.CurrentUser, dlg.TargetHive);
        });
    }

    [Fact]
    public void DetailsPanel_parses_with_the_application_resources()
    {
        // The source block and the shell-extension view live in this panel; a mistyped
        // StaticResource key compiles fine and only fails when the control is loaded.
        RunOnSta(() =>
        {
            var panel = new Views.Controls.DetailsPanel();
            Assert.NotNull(panel);
        });
    }

    [Fact]
    public void DetailsPanel_binding_targets_exist_on_the_view_model()
    {
        var expected = new[]
        {
            "SelectedItem", "SelectedExtension", "HasSelectedExtension", "HasSelectedItem",
            "EditPanel", "EditCommand", "ToggleEnabledCommand", "DeleteCommand", "AddChildCommand",
        };
        var missing = expected.Where(n => typeof(MainViewModel).GetProperty(n) is null).ToList();
        Assert.Empty(missing);
    }

    /// <summary>Runs <paramref name="work"/> on an STA thread and rethrows failures.</summary>
    private static void RunOnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
    }

    private static void EnsureApplicationResources()
    {
        // The pack:// scheme is only registered once an Application exists, so the
        // resource dictionary has to be built afterwards.
        var app = Application.Current ?? new Application();

        if (app.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("Styles.xaml") == true))
            return;

        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/RCMenuManager;component/Resources/Styles.xaml", UriKind.Absolute),
        });
    }
}
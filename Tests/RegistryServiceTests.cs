using System;
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class RegistryServiceTests
{
    /// <summary>
    /// Explorer does not stop at the ProgID: it keeps merging the perceived type
    /// class and finally the all-files classes. Without those steps the verb list
    /// for ".txt" is empty even though the context menu shows 打开 / 编辑.
    /// </summary>
    [Fact]
    public void Extension_scope_merges_perceived_type_and_all_files_classes()
    {
        var reg = new RegistryService();
        var paths = reg.GetShellQueryPaths(MenuScope.ForExtension(".txt", "txtfile"))
            .Select(p => p.SubKey)
            .ToList();

        Assert.Contains(paths, p => p.EndsWith(@"SystemFileAssociations\text\shell", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.EndsWith(@"*\shell", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.EndsWith(@"AllFilesystemObjects\shell", StringComparison.OrdinalIgnoreCase));

        // Extension-specific keys still come first so they win the de-duplication
        // and edits target the narrowest key available.
        var firstExtensionIndex = paths.FindIndex(p => p.EndsWith(@".txt\shell", StringComparison.OrdinalIgnoreCase));
        var globalIndex = paths.FindIndex(p => p.EndsWith(@"*\shell", StringComparison.OrdinalIgnoreCase));
        Assert.True(firstExtensionIndex >= 0 && firstExtensionIndex < globalIndex);
    }

    [Fact]
    public void Perceived_type_is_read_from_the_extension_key()
    {
        var reg = new RegistryService();

        Assert.Equal("text", reg.ResolvePerceivedType(".txt", "txtfile"));
        Assert.Equal("image", reg.ResolvePerceivedType(".png", "pngfile"));
        Assert.Null(reg.ResolvePerceivedType(".this-extension-does-not-exist", null));
    }

    [Fact]
    public void Every_scope_resolves_at_least_one_query_path()
    {
        var reg = new RegistryService();
        var scopes = new[]
        {
            MenuScope.AllFiles, MenuScope.AllFilesystemObjects, MenuScope.Folder,
            MenuScope.FolderBackground, MenuScope.Drive, MenuScope.Desktop,
            MenuScope.ForExtension(".txt", "txtfile"),
        };

        foreach (var scope in scopes)
            Assert.NotEmpty(reg.GetShellQueryPaths(scope));
    }
}

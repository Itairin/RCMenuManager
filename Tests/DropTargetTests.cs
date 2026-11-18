using System;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Dropping an item must (a) pick the right class scope and (b) be described honestly:
/// the list is read from the dropped object, but edits still write to the class key.
/// </summary>
public class DropTargetTests
{
    private readonly FileTypeService _types = new();

    [Fact]
    public void A_folder_is_identified_as_a_folder()
    {
        var dir = System.IO.Path.GetTempPath();
        var info = _types.Identify(dir);

        Assert.Equal(DragDropKind.Folder, info.Kind);
    }

    [Fact]
    public void A_file_is_identified_as_a_file()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rcm-drop-probe.txt");
        System.IO.File.WriteAllText(path, "x");
        try
        {
            var info = _types.Identify(path);
            Assert.Equal(DragDropKind.File, info.Kind);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void A_drive_root_is_identified_as_a_drive()
    {
        var info = _types.Identify(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'));

        Assert.Equal(DragDropKind.Drive, info.Kind);
    }

    [Fact]
    public void Missing_paths_are_not_a_valid_target()
    {
        var info = _types.Identify(@"Z:\definitely\not\here\thing.xyz");
        var target = new DropTarget(info.Path, DropTargetKind.Unknown);

        Assert.False(target.IsValid);
    }

    [Fact]
    public void The_effect_note_states_that_edits_are_class_wide()
    {
        var target = new DropTarget(@"C:\work\report.xlsx", DropTargetKind.File);

        Assert.Contains("这一类", target.EffectNote);
        Assert.Contains("文件", target.EffectNote);
    }

    [Fact]
    public void Labels_prefer_the_file_name_and_keep_folder_paths()
    {
        Assert.Equal("report.xlsx", new DropTarget(@"C:\work\report.xlsx", DropTargetKind.File).Label);
        Assert.Equal(@"D:\projects", new DropTarget(@"D:\projects", DropTargetKind.Folder).Label);
        Assert.Contains("背景", new DropTarget(@"D:\projects", DropTargetKind.FolderBackground).Label);
    }

    [Fact]
    public void Switching_to_the_background_menu_keeps_the_path()
    {
        var folder = new DropTarget(@"D:\projects", DropTargetKind.Folder);

        var background = folder with { Kind = DropTargetKind.FolderBackground };

        Assert.Equal(folder.Path, background.Path);
        Assert.Equal(DropTargetKind.FolderBackground, background.Kind);
    }
}

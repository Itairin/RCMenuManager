using System;

namespace RCMenuManager.Models;

public enum DropTargetKind
{
    Unknown,
    Drive,
    Folder,
    FolderBackground,
    File,
}

/// <summary>
/// A concrete item the user dropped on the window. The scope says <em>which class</em>
/// of menu to edit (and therefore where writes land); the path says <em>which object</em>
/// the menu is read from, so the list reflects that exact file or folder instead of a
/// scratch sample.
/// </summary>
public sealed record DropTarget(string Path, DropTargetKind Kind)
{
    public bool IsValid => Kind != DropTargetKind.Unknown && !string.IsNullOrEmpty(Path);

    /// <summary>Short label for the banner: file name, or the folder/drive path.</summary>
    public string Label => Kind switch
    {
        DropTargetKind.File => System.IO.Path.GetFileName(Path),
        DropTargetKind.Folder => Path,
        DropTargetKind.Drive => Path,
        DropTargetKind.FolderBackground => Path + " （背景）",
        _ => Path,
    };

    public string KindText => Kind switch
    {
        DropTargetKind.Drive => "驱动器",
        DropTargetKind.Folder => "文件夹",
        DropTargetKind.FolderBackground => "文件夹空白处",
        DropTargetKind.File => "文件",
        _ => "未知",
    };

    /// <summary>
    /// Honest statement of what an edit will touch: registry edits always land on the
    /// class-wide key, never on the single dropped item.
    /// </summary>
    public string EffectNote => Kind == DropTargetKind.Unknown
        ? string.Empty
        : "列表来自这个对象真实的右键菜单；但在这里做的修改写入的是「" + KindText + "」这一类，"
          + "对该类型的其他文件/文件夹同样生效。";
}

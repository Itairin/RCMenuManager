namespace RCMenuManager.Models;

/// <summary>
/// Logical context-menu scope. Maps to one or more registry roots.
/// </summary>
public enum ScopeType
{
    /// <summary>HKCR\*\shell - applies to every file.</summary>
    AllFiles,

    /// <summary>HKCR\AllFilesystemObjects\shell - applies to every file and folder.</summary>
    AllFilesystemObjects,

    /// <summary>HKCR\Directory\shell - applies when right-clicking a folder.</summary>
    Folder,

    /// <summary>HKCR\Directory\Background\shell - applies inside a folder's empty area.</summary>
    FolderBackground,

    /// <summary>HKCR\Drive\shell - applies to drives in Explorer.</summary>
    Drive,

    /// <summary>HKCR\DesktopBackground\Shell - applies on the desktop.</summary>
    Desktop,

    /// <summary>HKCR\&lt;.ext&gt;\shell (resolved through ProgID).</summary>
    FileExtension,
}
